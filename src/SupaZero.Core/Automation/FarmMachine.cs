using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public enum MacroPhase { Idle, WalkingToLobby, Lobby, Voting, Fighting, Navigating, Upgrading, Advancing, Retrying, Done }

/// <summary>
/// Otak AutoFarm: murni (tanpa I/O), menerima <see cref="GameSnapshot"/> + waktu, mengembalikan aksi. Mudah dites.
/// Alur: lobby (jalan ke lingkaran Create Room → pilih mode/map/act/difficulty → Start) → vote Yes → ronde (serang +
/// skill; area bersih → jalan ke area berikutnya, serang rubble bila tersangkut) → EndScreen: menang (upgrade bila perlu
/// → Next/Retry/Leave sesuai mode) / kalah (Retry s.d. batas).
/// </summary>
public sealed class FarmMachine(Func<AppSettings> settings)
{
    private readonly Dictionary<string, long> _cooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _skillLast = new(StringComparer.Ordinal);
    private long _lastAttack;
    private int _retries;
    private int _boughtThisStage;
    private bool _upgradeDoneThisStage;
    private bool _pendingBuyConfirm;
    private int? _tokensBeforeBuy;
    private GameScreen _lastScreen = GameScreen.Unknown;
    private Vec3? _stuckRef;
    private long _stuckSince;
    private long? _diffAt;
    private readonly Dictionary<string, int> _scrolls = new(StringComparer.Ordinal);

    public MacroPhase Phase { get; private set; } = MacroPhase.Idle;
    public int StagesCleared { get; private set; }
    public int Retries => _retries;
    public string Status { get; private set; } = "Idle";

    public void Reset()
    {
        _cooldowns.Clear();
        _roomBan.Clear();
        _scrolls.Clear();
        _diffAt = null;
        _roomTarget = null;
        _arrivedAt = null;
        _skillLast.Clear();
        _lastAttack = 0;
        _retries = _boughtThisStage = 0;
        _upgradeDoneThisStage = _pendingBuyConfirm = false;
        _tokensBeforeBuy = null;
        _lastScreen = GameScreen.Unknown;
        _stuckRef = null;
        StagesCleared = 0;
        Phase = MacroPhase.Idle;
        Status = "Idle";
    }

    public IReadOnlyList<MacroAction> Step(GameSnapshot s, long nowMs)
    {
        var S = settings();
        var screen = s.Screen;
        if (screen != _lastScreen) OnScreenChanged(_lastScreen, screen);
        _lastScreen = screen;

        var actions = screen switch
        {
            GameScreen.Unknown => Wait("Menunggu Roblox / DataModel."),
            GameScreen.Lobby => WalkToLobby(s, S, nowMs),
            GameScreen.LobbyMenu => LobbyMenu(s, S, nowMs),
            GameScreen.QueueWaiting => QueueWait(s, nowMs),
            GameScreen.StartVote => Vote(s, S, nowMs),
            GameScreen.InRound => Fight(s, S, nowMs),
            GameScreen.Victory => Victory(s, S, nowMs),
            GameScreen.Defeat => Defeat(s, S, nowMs),
            _ => [],
        };
        if (_pending.Count > 0) { actions = [.. _pending, .. actions]; _pending.Clear(); }
        // Lepas tombol gerak di semua fase yang tidak berjalan.
        if (Phase is not (MacroPhase.WalkingToLobby or MacroPhase.Navigating) && !actions.Any(a => a is MoveAction))
            return [new MoveAction([], "Berhenti jalan"), .. actions];
        return actions;
    }

    private void OnScreenChanged(GameScreen from, GameScreen to)
    {
        if (to == GameScreen.Victory)
        {
            StagesCleared++;
            if (_selectedAct > 0 && _selectedMap.Length > 0)
            {
                ActCleared?.Invoke(_selectedMap, _selectedAct);
                _selectedAct++; // Next stage = act berikutnya
            }
        }
        if (to == GameScreen.StartVote || (to == GameScreen.InRound && from is GameScreen.Victory or GameScreen.Defeat))
        {
            _boughtThisStage = 0;
            _upgradeDoneThisStage = false;
            _pendingBuyConfirm = false;
        }
        if (to == GameScreen.Victory) _retries = 0;
        if (to != GameScreen.LobbyMenu) { _diffAt = null; _scrolls.Clear(); }
        _stuckRef = null;
    }

    /// <summary>Layar tunggu room: klik "Skip Waiting" bila gratis; bila berbayar (Uses 1 Crystal) tunggu hitung mundur.</summary>
    private IReadOnlyList<MacroAction> QueueWait(GameSnapshot s, long now)
    {
        if (s.QueueSkip is not { } r) return [];
        if (s.QueueSkipCosts)
            return Note("queue-cost", now, 8000, "Skip Waiting butuh 1 Crystal → tidak diklik, menunggu hitung mundur.");
        if (!InWindow(s, r.CenterX, r.CenterY))
            return Note("queue-out", now, 5000, $"Tombol Skip Waiting di luar jendela ({r.CenterX:0},{r.CenterY:0}).");
        if (!Ready("queue-skip", now, 2000)) return [];
        Status = "Menunggu room, klik Skip Waiting.";
        return [new ClickAction(r.CenterX, r.CenterY, "Skip Waiting")];
    }

    private List<MacroAction> Wait(string why)
    {
        Phase = MacroPhase.Idle;
        Status = why;
        return [];
    }

    // ---------- Lobby ----------

    /// <summary>Lingkaran lebih jauh dari ini → teleport dulu lewat tombol Areas.</summary>
    public const double TeleportIfFartherThan = 45;

    /// <summary>
    /// Lobby: lingkaran "Start Here" terdekat ≤ 45 stud → jalan ke sana. Lebih jauh / tidak terbaca → tombol Areas →
    /// kartu teleport (Story) → muncul dekat lingkaran. Posisi rekaman manual hanya cadangan.
    /// </summary>
    private readonly Dictionary<string, long> _roomBan = new(StringComparer.Ordinal);
    private string? _roomTarget;
    private long? _arrivedAt;

    private static string Label(string id) => id.StartsWith("room:") ? "room " + id[5..] : id == "manual" ? "lingkaran rekaman" : "lingkaran";

    private List<MacroAction> WalkToLobby(GameSnapshot s, AppSettings S, long now)
    {
        var f = S.Farm;
        if (!f.AutoCreateLobby) return Wait("Di lobby (Auto create room mati).");
        Phase = MacroPhase.WalkingToLobby;

        // Kandidat: room QuqueRooms (lewati yang terisi pemain lain), cadangan "Start Here", lalu rekaman manual.
        var cands = new List<(string Id, Vec3 Pos)>();
        foreach (var r in s.StartRooms)
        {
            if (f.SkipOccupiedRooms && r.Occupied) continue;
            cands.Add(("room:" + r.Id, r.Pos));
        }
        if (s.StartRooms.Count == 0)
            for (int i = 0; i < s.StartCircles.Count; i++) cands.Add(("circle:" + i, s.StartCircles[i]));
        if (f.LobbyCircle is { } manual) cands.Add(("manual", manual));

        // Room yang gagal join (sudah sampai tapi menu tidak terbuka) di-skip 30 dtk.
        foreach (var k in _roomBan.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList()) _roomBan.Remove(k);
        var usable = cands.Where(c => !_roomBan.ContainsKey(c.Id)).ToList();
        if (usable.Count == 0 && cands.Count > 0)
        {
            _roomBan.Clear(); // semua sudah dicoba → ulang dari awal
            usable = cands;
        }

        (string Id, Vec3 Pos)? target = null;
        double dist = double.MaxValue;
        if (s.PlayerPos is { } p)
        {
            // Tetap di room yang sedang dituju selama masih valid (hindari bolak-balik).
            if (_roomTarget is { } cur && usable.FirstOrDefault(c => c.Id == cur) is { Id: not null } keep)
            {
                target = keep;
                dist = Navigator.Distance2D(p.X, p.Z, keep.Pos.X, keep.Pos.Z);
            }
            else
                foreach (var c in usable)
                {
                    double d = Navigator.Distance2D(p.X, p.Z, c.Pos.X, c.Pos.Z);
                    if (d < dist) { dist = d; target = c; }
                }
        }
        var pre = new List<MacroAction>();
        if (_roomTarget is not null && target?.Id != _roomTarget)
        {
            if (s.StartRooms.Any(r => "room:" + r.Id == _roomTarget && r.Occupied))
                pre.Add(new NoteAction($"{Label(_roomTarget)} diisi pemain lain → pindah lingkaran."));
            _roomTarget = null;
            _arrivedAt = null;
        }

        if (target is { } t && dist <= TeleportIfFartherThan)
        {
            _roomTarget = t.Id;
            var walk = WalkTo(s, t.Pos, S.Navigation.ArriveRadius, now, "lingkaran Create Room", attackWhenStuck: false);
            if (walk.Count > 0) { _arrivedAt = null; return [.. pre, .. walk]; }

            _arrivedAt ??= now;
            long waited = now - _arrivedAt.Value;
            if (waited >= Math.Max(1, f.JoinTimeoutSec) * 1000L)
            {
                _roomBan[t.Id] = now + 30_000;
                _roomTarget = null;
                _arrivedAt = null;
                Status = "Gagal join, pindah lingkaran.";
                return [new MoveAction([], "Pindah lingkaran"), new NoteAction($"Tidak bisa join {Label(t.Id)} ({waited / 1000} dtk) → coba lingkaran lain.")];
            }
            Status = $"Di {Label(t.Id)}, menunggu menu Create Room ({waited / 1000}/{f.JoinTimeoutSec} dtk).";
            return [new MoveAction([], "Sampai")];
        }
        _roomTarget = null;
        _arrivedAt = null;
        if (target is null && s.StartRooms.Count > 0 && s.StartRooms.All(r => r.Occupied) && f.SkipOccupiedRooms)
        {
            Status = "Semua room terisi pemain lain, menunggu yang kosong.";
            return [.. pre, new MoveAction([], "Tunggu room kosong"), .. Note("rooms-full", now, 10000, "Semua room terisi pemain lain, menunggu yang kosong.")];
        }

        // Teleport lewat Areas → kartu sesuai mode (Story bila tidak ada kartu bernama mode itu).
        var card = s.TeleportCards.FirstOrDefault(c => Matches(c.Name, f.Mode))
                   ?? s.TeleportCards.FirstOrDefault(c => Matches(c.Name, nameof(FarmMode.Story)));
        if (card?.Rect is { } cr)
        {
            Status = $"Teleport: Areas → {card.Name}";
            return Ready("tp-card", now, 2500) ? [new MoveAction([], "Teleport"), new ClickAction(cr.CenterX, cr.CenterY, $"Teleport {card.Name}")] : [];
        }
        if (s.AreasButton is { } ab)
        {
            Status = "Buka Areas untuk teleport.";
            return Ready("areas", now, 1500) ? [new MoveAction([], "Teleport"), new ClickAction(ab.CenterX, ab.CenterY, "Tombol Areas")] : [];
        }
        Status = "Lingkaran Create Room dan tombol Areas tidak terbaca.";
        return [new MoveAction([], "Tunggu"), .. Note("no-hub", now, 8000, "Tombol Areas / lingkaran 'Start Here' tidak terbaca. Cek di RIE atau rekam posisi lingkaran manual.")];
    }

    private List<MacroAction> LobbyMenu(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Lobby;
        var f = S.Farm;
        if (!f.AutoCreateLobby) return Wait("Menu Create Room terbuka (Auto create room mati).");

        // 1) Mode
        if (s.LobbyModes.Count > 0 && Pick(s, s.LobbyModes, s.LobbyModesView, f.Mode, "mode", now) is { } modeAct) return modeAct;
        // 2) Map. Map kosong = map terpilih, tapi kalau mode map itu tidak cocok → map pertama (terbuka) dengan mode yang dipilih.
        string targetMap = f.Map;
        if (string.IsNullOrWhiteSpace(targetMap) && s.LobbyMaps.Any(m => m.Mode is not null))
        {
            var cur = s.LobbyMaps.FirstOrDefault(m => Matches(s.LobbyMapTag, m.Name)) ?? s.LobbyMaps.FirstOrDefault(m => m.Selected);
            if (cur?.Mode is { } cm && !Matches(cm, f.Mode))
            {
                var byMode = s.LobbyMaps.FirstOrDefault(m => !m.Locked && m.Mode is { } mm && Matches(mm, f.Mode));
                if (byMode is null) return Stop($"Tidak ada map terbuka untuk mode {f.Mode}.");
                targetMap = byMode.Name;
            }
        }
        f = f with { Map = targetMap };
        if (!string.IsNullOrWhiteSpace(f.Map) && !Matches(s.LobbyMapTag, f.Map))
        {
            Status = $"Map terpilih '{s.LobbyMapTag ?? "?"}', target '{f.Map}'.";
            var entry = s.LobbyMaps.FirstOrDefault(e => Matches(e.Name, f.Map));
            if (entry is { Locked: true }) return Stop($"Map '{f.Map}' masih terkunci di akun ini.");
            if (entry is not null && ClickEntry(s, entry, s.LobbyMapsView, "lobby-map", $"Map {entry.Name}", now) is { } mapAct) return mapAct;
            return Note("lobby-map", now, 5000, $"Map '{f.Map}' tidak ada di MapsList (terbaca: {string.Join(", ", s.LobbyMaps.Select(m => m.Name))}).");
        }
        // 3) Act. Daftar act tergantung map (Training cuma Act 1) → target disesuaikan dengan act yang ADA di ActsList.
        string mapKey = (f.Map.Length > 0 ? f.Map : s.LobbyMapTag ?? "").Trim();
        var acts = s.LobbyActs.Select(e => (Entry: e, No: ActNumber(e.Name))).Where(x => x.No > 0).OrderBy(x => x.No).ToList();
        int want = f.Act;
        if (f.Act == 0) // Semua act berurutan: act setelah yang terakhir tuntas
        {
            int done = f.ActProgress?.GetValueOrDefault(mapKey) ?? 0;
            want = done + 1;
            if (acts.Count > 0 && want > acts[^1].No)
                return Stop($"Semua act di map '{mapKey}' sudah tuntas (Act 1–{acts[^1].No}).");
        }
        if (acts.Count > 0 && !acts.Any(a => a.No == want))
        {
            int fallback = acts.Where(a => a.No <= want && !a.Entry.Locked).Select(a => a.No).DefaultIfEmpty(acts[0].No).Max();
            _pending.AddRange(Note("act-clamp", now, 15000, $"Act {want} tidak ada di map '{mapKey}' (tersedia: {string.Join(", ", acts.Select(a => a.No))}) → pakai Act {fallback}."));
            want = fallback;
        }
        _selectedAct = want;
        _selectedMap = mapKey;
        if (ActNumber(s.LobbyActTag) != want)
        {
            Status = $"Act terpilih '{s.LobbyActTag ?? "?"}', target Act {want}.";
            var entry = acts.FirstOrDefault(a => a.No == want).Entry;
            if (entry is { Locked: true }) return Stop($"Act {want} di map '{mapKey}' masih terkunci.");
            if (entry is not null && ClickEntry(s, entry, s.LobbyActsView, "lobby-act", $"Act {want}", now) is { } actAct) return actAct;
            return Note("lobby-act", now, 5000, $"Act {want} tidak terbaca di ActsList (ActLine.Tag = '{s.LobbyActTag ?? "?"}').");
        }
        string wantAct = "Act " + want;
        // 4) Difficulty (klik dulu, tunggu terdaftar) → Start
        Status = $"Create Room: {f.Mode} {(f.Map.Length > 0 ? f.Map : s.LobbyMapTag)} {wantAct} {f.Difficulty} → Start.";
        bool diffKnown = !string.IsNullOrEmpty(s.LobbyDifficultyTag);
        bool diffOk = diffKnown && s.LobbyDifficultyTag!.Equals(f.Difficulty.ToString(), StringComparison.OrdinalIgnoreCase);
        if (diffKnown ? !diffOk : (_diffAt is null || now - _diffAt.Value >= 8000))
        {
            var (dx, dy) = DifficultyPoint(s, f.Difficulty);
            if (!InWindow(s, dx, dy))
                return Note("lobby-diff", now, 5000, $"Tombol difficulty {f.Difficulty} di luar jendela Roblox ({dx:0},{dy:0}) → tidak diklik.");
            if (diffKnown && !Ready("lobby-diff", now, 1500)) return [];
            _diffAt = now;
            Status = $"Difficulty terpilih '{s.LobbyDifficultyTag ?? "?"}', target {f.Difficulty}.";
            return [new ClickAction(dx, dy, $"Difficulty {f.Difficulty}")];
        }
        if (_diffAt is { } da && now - da < 700) return [];
        var (sx, sy) = s.LobbyStart is { } st ? (st.CenterX, st.CenterY) : Scaled(s, AnimeZeroPaths.StartRefX, AnimeZeroPaths.StartRefY);
        if (!InWindow(s, sx, sy))
            return Note("lobby-start", now, 5000, $"Tombol Start di luar jendela Roblox ({sx:0},{sy:0}) → tidak diklik.");
        return Ready("lobby-start", now, 3000) ? [new ClickAction(sx, sy, s.LobbyStart is null ? "Start (posisi cadangan)" : "Start (Create Room)")] : [];
    }

    private static (double X, double Y) DifficultyPoint(GameSnapshot s, Difficulty d)
    {
        var hit = s.LobbyDifficulties.FirstOrDefault(e => e.Name.Equals(d.ToString(), StringComparison.OrdinalIgnoreCase));
        if (hit?.Rect is { } r) return (r.CenterX, r.CenterY);
        var (x, y) = AnimeZeroPaths.DifficultyCenters[(int)d];
        return Scaled(s, x, y);
    }

    /// <summary>Koordinat acuan 1920x1080 → ukuran klien jendela sekarang (tanpa ukuran = apa adanya).</summary>
    private static (double X, double Y) Scaled(GameSnapshot s, double x, double y) =>
        s.ClientW > 0 && s.ClientH > 0 ? (x * s.ClientW / AnimeZeroPaths.RefW, y * s.ClientH / AnimeZeroPaths.RefH) : (x, y);

    /// <summary>Titik ada di dalam area klien jendela Roblox (ukuran tidak diketahui = dianggap di dalam).</summary>
    public static bool InWindow(GameSnapshot s, double x, double y) =>
        s.ClientW <= 0 || s.ClientH <= 0 || (x >= 2 && y >= 2 && x <= s.ClientW - 2 && y <= s.ClientH - 2);

    /// <summary>
    /// Klik entri daftar (map/act/mode). Kalau entri berada di luar area tampak daftarnya (perlu di-scroll) atau di luar
    /// jendela: scroll roda mouse di atas daftar ke arah entri, baca ulang, baru klik setelah terlihat. Tidak pernah
    /// mengklik di luar jendela Roblox. null = entri tanpa rect.
    /// </summary>
    private List<MacroAction>? ClickEntry(GameSnapshot s, LobbyEntry e, GuiRect? view, string key, string label, long now)
    {
        if (e.Rect is not { } r) return null;
        double margin = view is { } v0 ? Math.Min(8, Math.Min(v0.W, v0.H) / 4) : 0;
        bool inView = view is not { } vw || vw.Contains(r.CenterX, r.CenterY, margin);
        if (inView && InWindow(s, r.CenterX, r.CenterY))
        {
            _scrolls.Remove(key);
            return Ready(key, now, 2500) ? [new ClickAction(r.CenterX, r.CenterY, label)] : [];
        }
        if (view is not { } v)
            return Note(key + "-out", now, 5000, $"{label} di luar jendela Roblox ({r.CenterX:0},{r.CenterY:0}) dan area daftar tidak terbaca → tidak diklik.");

        int n = _scrolls.GetValueOrDefault(key);
        if (n >= 40)
            return Note(key + "-scroll", now, 5000, $"{label} tetap tidak terlihat setelah {n} kali scroll → tidak diklik. Cek ukuran jendela Roblox.");
        // Bagian daftar yang benar-benar tampak = rect daftar ∩ jendela.
        double top = v.Y, bottom = v.Bottom, left = v.X, right = v.Right;
        if (s.ClientW > 0 && s.ClientH > 0)
        {
            top = Math.Max(top, 0); bottom = Math.Min(bottom, s.ClientH); left = Math.Max(left, 0); right = Math.Min(right, s.ClientW);
            if (bottom - top < 8 || right - left < 8)
                return Note(key + "-out", now, 5000, $"Daftar untuk {label} di luar jendela Roblox → tidak di-scroll/klik.");
        }
        // Target di bawah/kanan → scroll ke bawah (notch negatif); di atas/kiri → ke atas.
        bool vertical = r.CenterY < top + margin || r.CenterY > bottom - margin;
        double dist = vertical ? (r.CenterY > bottom - margin ? r.CenterY - (bottom - margin) : r.CenterY - (top + margin))
                               : (r.CenterX > right - margin ? r.CenterX - (right - margin) : r.CenterX - (left + margin));
        double span = Math.Max(40, (vertical ? bottom - top : right - left) * 0.5);
        int notches = (int)Math.Clamp(Math.Ceiling(Math.Abs(dist) / span), 1, 3) * (dist > 0 ? -1 : 1);
        double cx = (left + right) / 2, cy = (top + bottom) / 2;
        if (!Ready(key + "-scroll", now, 450)) return [];
        _scrolls[key] = n + 1;
        Status = $"Scroll daftar ke {label}.";
        return [new ScrollAction(cx, cy, notches, $"{label} belum terlihat → scroll {(notches < 0 ? "bawah" : "atas")}")];
    }

    /// <summary>Klik entri yang cocok bila belum terpilih. null = sudah benar / tidak ada data.</summary>
    private List<MacroAction>? Pick(GameSnapshot s, IReadOnlyList<LobbyEntry> list, GuiRect? view, string want, string what, long now)
    {
        var entry = list.FirstOrDefault(e => Matches(e.Name, want));
        if (entry is null || entry.Selected) return null;
        if (entry.Locked) return Stop($"{what} '{want}' masih terkunci di akun ini.");
        Status = $"Pilih {what} {want}.";
        return ClickEntry(s, entry, view, "lobby-" + what, $"{what} {entry.Name}", now) ?? [];
    }

    private List<MacroAction> Vote(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Voting;
        Status = "Start Game? → Yes";
        if (!S.Farm.AutoVoteStart || !Ready("vote-yes", now, 2000)) return [];
        var (x, y) = s.StartVoteYes is { } r ? (r.CenterX, r.CenterY) : (AnimeZeroPaths.StartVoteYesX, AnimeZeroPaths.StartVoteYesY);
        return [new ClickAction(x, y, "Vote Yes")];
    }

    // ---------- Ronde ----------

    private List<MacroAction> Fight(GameSnapshot s, AppSettings S, long now)
    {
        var hud = s.Hud;
        int left = s.EnemyCount >= 0 ? s.EnemyCount : hud?.EnemiesLeft ?? -1;
        string area = hud is { AreaTotal: > 0 } ? $"Area {hud.Area}/{hud.AreaTotal}" : "Area ?";
        if (left == 0)
        {
            Phase = MacroPhase.Navigating;
            if (!S.Navigation.AutoNavigate) { Status = $"{area} bersih (Auto navigasi mati)."; return []; }
            if (s.NextAreaPos is not { } next)
            {
                Status = $"{area} bersih, area berikutnya tidak terbaca.";
                return [new MoveAction([], "Tujuan tidak ada"), .. Note("nav-none", now, 6000, $"{area} bersih tapi posisi area berikutnya (World.Spawns/Areas) tidak terbaca.")];
            }
            var walk = WalkTo(s, next, S.Navigation.ArriveRadius, now, $"area {hud!.Area + 1}", S.Navigation.AttackWhenStuck);
            Status = walk.Count == 0 ? $"Sampai area {hud!.Area + 1}, menunggu musuh." : $"{area} bersih → jalan ke area {hud!.Area + 1}.";
            return walk.Count == 0 ? [new MoveAction([], "Sampai")] : walk;
        }
        Phase = MacroPhase.Fighting;
        Status = $"{area} · {(left < 0 ? "?" : left.ToString())} musuh";
        var actions = new List<MacroAction>();
        // Tracking: musuh hidup terdekat → jalan mendekat sampai jarak serang, kursor diarahkan ke musuh.
        // Selama mendekat hanya Dash (Q) yang dipakai; serangan dasar + skill 1/2/3 baru setelah dalam jarak serang.
        bool approaching = false;
        EnemyInfo? tgt = null;
        if (s.PlayerPos is { } me && s.Enemies.Where(e => e.Alive)
                .OrderBy(e => Navigator.Distance2D(me.X, me.Z, e.Pos.X, e.Pos.Z)).FirstOrDefault() is { } target)
        {
            tgt = target;
            double d = Navigator.Distance2D(me.X, me.Z, target.Pos.X, target.Pos.Z);
            Status += $" · target #{target.Name} {d:0} stud";
            if (S.Navigation.ChaseEnemies)
            {
                approaching = d > S.Navigation.AttackRange;
                var walk = approaching ? WalkTo(s, target.Pos, S.Navigation.AttackRange, now, $"musuh #{target.Name}", false) : [];
                actions.AddRange(walk.Count > 0 ? walk : [new MoveAction([], "Dekat musuh")]);
            }
            if (S.Navigation.AimAtEnemy && s.ShiftLock)
            {
                // Shift lock: kursor terkunci di tengah → putar kamera (yaw) ke arah musuh dengan gerak relatif.
                // Yaw dan pitch dihitung dari posisi kamera (shift lock menggeser kamera ke kanan bahu); pitch dijaga -40°..+10°.
                int dx = 0, dy = 0;
                if (s.Camera is { } cam)
                {
                    dx = Turn(cam.Look.X, cam.Look.Z, target.Pos.X - cam.Pos.X, target.Pos.Z - cam.Pos.Z, S.Navigation.TurnPixelsPerDegree);
                    dy = Pitch(cam.Look, target.Pos.X - cam.Pos.X, target.Pos.Y - cam.Pos.Y, target.Pos.Z - cam.Pos.Z, S.Navigation.TurnPixelsPerDegree);
                }
                else if (s.CameraLook is { } cl)
                    dx = Turn(cl.X, cl.Z, target.Pos.X - me.X, target.Pos.Z - me.Z, S.Navigation.TurnPixelsPerDegree);
                if (dx != 0 || dy != 0)
                    actions.Add(new TurnAction(dx, dy, $"Putar kamera ke musuh #{target.Name}"));
            }
            else if (S.Navigation.AimAtEnemy && s.Camera is { } cam2 && Ready("cam-drag", now, 700))
            {
                // Tanpa shift lock: skill mengikuti kursor, jadi kamera ikut diputar (klik kanan tahan + geser)
                // bila musuh melenceng > 20° dari tengah atau pitch keluar batas -40°..+10°.
                double ex = target.Pos.X - cam2.Pos.X, ey = target.Pos.Y - cam2.Pos.Y, ez = target.Pos.Z - cam2.Pos.Z;
                int tdx = Turn(cam2.Look.X, cam2.Look.Z, ex, ez, S.Navigation.TurnPixelsPerDegree, gain: 1, deadzone: 20, max: 200);
                int tdy = Pitch(cam2.Look, ex, ey, ez, S.Navigation.TurnPixelsPerDegree, gain: 1, deadzone: 8, max: 150);
                if (tdx != 0 || tdy != 0)
                    actions.Add(new TurnAction(tdx, tdy, $"Putar kamera (klik kanan) ke musuh #{target.Name}", Drag: true));
                else _cooldowns.Remove("cam-drag");
            }
            if (S.Navigation.AimAtEnemy && !s.ShiftLock && s.Camera?.WorldToScreen(target.Pos) is { } sp
                && sp.X >= 0 && sp.Y >= 0 && sp.X < s.Camera.ViewW && sp.Y < s.Camera.ViewH)
                actions.Add(new AimAction(sp.X, sp.Y, $"Arahkan ke musuh #{target.Name}"));
        }

        var skills = S.Combat.EffectiveSkills.Where(k => k.Enabled && HotkeyMap.TryGetVirtualKey(k.Key, out _)).ToList();
        var dash = skills.FirstOrDefault(IsDash);
        var swap = skills.FirstOrDefault(IsSwap);
        if (approaching)
        {
            // Dash hanya bila masih cukup jauh (> 2× jarak serang) dan tidak sedang cooldown (HUD Dash.Progress).
            if (dash is not null && !s.DashCooling && Ready("dash", now, Math.Max(800, dash.CooldownMs))
                && s.PlayerPos is { } p0 && actions.OfType<MoveAction>().Any(m => m.Keys.Count > 0)
                && s.Enemies.Any(e => e.Alive && Navigator.Distance2D(p0.X, p0.Z, e.Pos.X, e.Pos.Z) > 2 * S.Navigation.AttackRange)
                && HotkeyMap.TryGetVirtualKey(dash.Key, out uint dvk))
                actions.Add(new KeyAction((ushort)dvk, 40, $"{dash.Name} ({dash.Key}) mendekat"));
            return actions;
        }

        // Skill 1/2/3: satu per tick, yang paling lama tidak dipakai dan siap. Bila HUD terbaca, cooldown/kunci dari HUD
        // (jeda minimal 400 ms agar HUD sempat berubah); bila tidak, pakai CooldownMs dari pengaturan.
        var hudCd = s.SkillCooldowns.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        bool SkillReady(SkillBinding k) => hudCd.TryGetValue(k.Key, out var c)
            ? c.Ready && (!_skillLast.TryGetValue(k.Name, out long t0) || now - t0 >= 400)
            : !_skillLast.TryGetValue(k.Name, out long t) || now - t >= Math.Max(200, k.CooldownMs);
        var main = skills.Where(k => !IsDash(k) && !IsSwap(k)).ToList();
        var due = main.Where(SkillReady)
            .OrderBy(k => _skillLast.TryGetValue(k.Name, out long t) ? t : long.MinValue)
            .FirstOrDefault();
        // Basic attack (klik terus-menerus) bisa menelan tombol skill/T di game, jadi saat skill atau T ditekan,
        // basic attack dilewati di tick itu dan ditahan sebentar sesudahnya.
        if (due is not null && HotkeyMap.TryGetVirtualKey(due.Key, out uint vk))
        {
            _skillLast[due.Name] = now;
            _attackHoldUntil = now + 250;
            actions.Add(new KeyAction((ushort)vk, 60, $"{due.Name} ({due.Key})"));
        }
        // Ganti power (T) hanya saat skill 1, 2, 3 di HUD semuanya cooldown/terkunci. T ditolak game selama animasi
        // serangan masih jalan, jadi: berhenti basic attack dulu (gerak tetap), tunggu animasi selesai, baru tekan T.
        // Bila HUD masih "semua cooldown" 1,2 dtk setelah T, ulangi (maks 3×), lalu jeda 4 dtk.
        else if (swap is not null && s.SkillCooldowns.Count > 0 && s.SkillCooldowns.All(c => !c.Ready)
                 && HotkeyMap.TryGetVirtualKey(swap.Key, out uint svk) && now >= _swapRestUntil)
        {
            if (_swapPrepSince is not long since)
            {
                _swapPrepSince = since = now;
                actions.Add(new NoteAction($"Semua skill cooldown → berhenti serang, siap {swap.Name} ({swap.Key})."));
            }
            _attackHoldUntil = now + 200;
            if (now - since >= SwapPrepMs && now - _swapLastPress >= 1200)
            {
                if (_swapTries >= 3) { _swapTries = 0; _swapPrepSince = null; _swapRestUntil = now + 4000; }
                else
                {
                    _swapTries++;
                    _swapLastPress = now;
                    actions.Add(new KeyAction((ushort)svk, 120, $"{swap.Name} ({swap.Key}): semua skill cooldown (coba {_swapTries})"));
                }
            }
        }
        else if (ResetSwap() && now >= _attackHoldUntil && now - _lastAttack >= Math.Max(30, S.Combat.AttackIntervalMs))
        {
            _lastAttack = now;
            actions.Add(new AttackAction("Serangan dasar"));
        }
        if (swap is null && s.SkillCooldowns.Count > 0 && s.SkillCooldowns.All(c => !c.Ready)
            && S.Combat.EffectiveSkills.FirstOrDefault(IsSwap) is { Enabled: false } off && Ready("swap-off", now, 15000))
            actions.Add(new NoteAction($"Semua skill cooldown, tapi {off.Name} ({off.Key}) mati di kartu Combat → nyalakan agar power berganti."));
        // Sudah dekat: tetap bergerak melingkari musuh + lompat (baik saat pakai skill maupun basic attack saja).
        Roam(s, S, tgt, now, actions);
        return actions;
    }

    /// <summary>
    /// Sudah dalam jarak serang: jalan melingkari musuh (radius ~0,7× jarak serang, ±80°/dtk)
    /// sambil lompat tiap 0,9–1,6 dtk, bukan diam di tempat.
    /// </summary>
    private void Roam(GameSnapshot s, AppSettings S, EnemyInfo? target, long now, List<MacroAction> actions)
    {
        if (!S.Navigation.RoamWhenSkillsCooling || !S.Navigation.ChaseEnemies || target is null
            || s.PlayerPos is not { } p || s.CameraLook is not { } look) return;
        double r = Math.Max(2.5, S.Navigation.AttackRange * 0.7);
        if (_roamSince is not long t0 || now - _roamLast > 1500) { t0 = now; _roamSince = now; _roamAngle = Math.Atan2(p.Z - target.Pos.Z, p.X - target.Pos.X); }
        _roamLast = now;
        // Arah putar kanan/kiri berganti acak tiap 2,5–4,5 dtk.
        if (now >= _roamFlipAt)
        {
            if (_roamFlipAt != 0) { _roamAngle += _roamDir * (now - t0) / 1000.0 * (80 * Math.PI / 180); _roamSince = t0 = now; _roamDir = -_roamDir; }
            _roamFlipAt = now + 2500 + _rng.Next(2000);
        }
        double a = _roamAngle + _roamDir * (Math.PI / 4 + (now - t0) / 1000.0 * (80 * Math.PI / 180)); // 45° di depan agar langsung bergerak
        double gx = target.Pos.X + r * Math.Cos(a), gz = target.Pos.Z + r * Math.Sin(a);
        var keys = Navigator.KeysToward(p.X, p.Z, look.X, look.Z, gx, gz, 0.8);
        actions.RemoveAll(x => x is MoveAction);
        actions.Insert(0, new MoveAction(keys, "Roaming melingkari musuh"));
        if (now >= _nextRoamJump)
        {
            _nextRoamJump = now + 700 + _rng.Next(600);
            // Lompat ditolak game selama animasi serang: buang klik serangan di tick ini dan tahan serangan 300 ms.
            actions.RemoveAll(x => x is AttackAction);
            _attackHoldUntil = Math.Max(_attackHoldUntil, now + 300);
            actions.Add(new KeyAction(VirtualKeys.Space, 100, "Lompat (roaming)"));
        }
    }
    private long _attackHoldUntil, _swapLastPress = long.MinValue / 2, _swapRestUntil;
    private long? _swapPrepSince;
    private int _swapTries;
    /// <summary>Lama berhenti basic attack sebelum T agar animasi serangan selesai.</summary>
    private const int SwapPrepMs = 900;
    private bool ResetSwap() { _swapPrepSince = null; _swapTries = 0; return true; }
    private long? _roamSince;
    private long _roamLast, _nextRoamJump, _roamFlipAt;
    private int _roamDir = 1;
    private double _roamAngle;
    private readonly Random _rng = new();

    /// <summary>Jalan ke tujuan dengan W/A/S/D; tersangkut ≥1,5 dtk → lompat (+ serang bila diizinkan, untuk rubble).</summary>
    private List<MacroAction> WalkTo(GameSnapshot s, Vec3 target, double radius, long now, string label, bool attackWhenStuck)
    {
        if (s.PlayerPos is not { } p || s.CameraLook is not { } look)
        {
            Status = "Posisi karakter/kamera tidak terbaca.";
            return [new MoveAction([], "Data posisi tidak ada"), .. Note("nopos", now, 6000, "Posisi karakter atau kamera tidak terbaca (cek offsets).")];
        }
        var keys = Navigator.KeysToward(p.X, p.Z, look.X, look.Z, target.X, target.Z, radius);
        if (keys.Count == 0) { _stuckRef = null; return []; }
        var actions = new List<MacroAction> { new MoveAction(keys, $"Jalan ke {label}") };
        if (_stuckRef is null || Navigator.Distance2D(p.X, p.Z, _stuckRef.X, _stuckRef.Z) > 1.5)
        {
            _stuckRef = p;
            _stuckSince = now;
        }
        else if (now - _stuckSince >= 1500)
        {
            if (Ready("jump", now, 1200)) actions.Add(new KeyAction(VirtualKeys.Space, 80, "Lompat (tersangkut)"));
            if (attackWhenStuck && now - _lastAttack >= 150)
            {
                _lastAttack = now;
                actions.Add(new AttackAction("Serang rintangan"));
            }
        }
        Status = $"Jalan ke {label} ({Navigator.Distance2D(p.X, p.Z, target.X, target.Z):0} stud)";
        return actions;
    }

    // ---------- Layar akhir ----------

    private List<MacroAction> Victory(GameSnapshot s, AppSettings S, long now)
    {
        var up = S.Upgrade;
        if (up.Enabled && !_upgradeDoneThisStage && Upgrade(s, up, now) is { } upgrade) return upgrade;

        var after = S.Farm.AfterWin == AfterWin.Auto
            ? (Matches(S.Farm.Mode, nameof(FarmMode.Story)) ? AfterWin.Next : AfterWin.Retry)
            : S.Farm.AfterWin;
        GuiRect? btn; string label;
        (btn, label) = after switch
        {
            AfterWin.Next => (s.NextButton, "Next stage"),
            AfterWin.Retry => (s.RetryButton, "Retry"),
            _ => (s.LeaveButton, "Leave"),
        };
        if (S.Farm.Act == 0) (after, btn, label) = s.NextButton is { } nb ? (AfterWin.Next, (GuiRect?)nb, "Next act") : (AfterWin.Leave, s.LeaveButton, "Leave");
        if (after == AfterWin.Next && btn is null && s.RetryButton is null && s.LeaveButton is not null)
            return Stop("Menang, tombol Next tidak ada → Story selesai.");
        if (btn is not { } b)
        {
            Phase = MacroPhase.Idle;
            Status = $"Menang, tombol {label} tidak terbaca.";
            return Note("no-btn", now, 5000, $"Tombol {label} tidak terbaca di EndScreen.");
        }
        Phase = MacroPhase.Advancing;
        Status = $"Menang (ke-{StagesCleared}) → {label}";
        return Ready("after-win", now, 2500) ? [new ClickAction(b.CenterX, b.CenterY, label)] : [];
    }

    /// <summary>null = upgrade selesai/tidak bisa, lanjut.</summary>
    private List<MacroAction>? Upgrade(GameSnapshot s, UpgradeSettings up, long now)
    {
        Phase = MacroPhase.Upgrading;
        if (!s.SkillTreeOpen)
        {
            _upgradeDoneThisStage = true;
            return [new NoteAction("SkillTree tidak terbuka (tombol pembukanya belum diriset) → upgrade stage ini dilewati.")];
        }
        if (_pendingBuyConfirm)
        {
            if (s.Tokens is int t && _tokensBeforeBuy is int b && t < b) { _boughtThisStage++; _pendingBuyConfirm = false; }
            else if (!Ready("buy-timeout", now, 3000)) return [];
            else _pendingBuyConfirm = false;
        }
        if (_boughtThisStage >= up.MaxBuysPerStage || s.Tokens is 0) { _upgradeDoneThisStage = true; return null; }
        if (s.BuyButton is { } buy && Ready("buy", now, 800))
        {
            _pendingBuyConfirm = true;
            _tokensBeforeBuy = s.Tokens;
            _cooldowns["buy-timeout"] = now;
            Status = $"Upgrade: beli (token {s.Tokens?.ToString() ?? "?"})";
            return [new ClickAction(buy.CenterX, buy.CenterY, "BuyButton")];
        }
        var slot = UpgradePlanner.Next(s.Slots, up.EffectivePriority, up.Mode, _boughtThisStage);
        if (slot is null) { _upgradeDoneThisStage = true; return null; }
        Status = $"Upgrade: pilih {slot.Column} #{slot.Index + 1}";
        return Ready("slot", now, 800) ? [new ClickAction(slot.Rect!.Value.CenterX, slot.Rect.Value.CenterY, $"Slot {slot.Column} #{slot.Index + 1}")] : [];
    }

    private List<MacroAction> Defeat(GameSnapshot s, AppSettings S, long now)
    {
        var f = S.Farm;
        if (f.RetryOnDefeat && _retries < f.MaxRetriesPerStage && s.RetryButton is { } retry)
        {
            Phase = MacroPhase.Retrying;
            Status = $"DEFEAT → Retry ({_retries + 1}/{f.MaxRetriesPerStage})";
            if (!Ready("retry", now, 3000)) return [];
            _retries++;
            return [new ClickAction(retry.CenterX, retry.CenterY, "Retry")];
        }
        return Stop("DEFEAT, retry habis → macro berhenti.");
    }

    // ---------- util ----------

    private List<MacroAction> Stop(string why)
    {
        Phase = MacroPhase.Done;
        Status = why;
        return [new MoveAction([], "Stop"), new StopAction(why)];
    }

    private List<MacroAction> Note(string key, long now, int everyMs, string text) =>
        Ready("note:" + key, now, everyMs) ? [new NoteAction(text)] : [];

    /// <summary>
    /// Delta X mouse untuk memutar yaw kamera dari arah (lx,lz) ke (tx,tz). Mouse ke kanan = kamera ke kanan.
    /// Toleransi 2°, setengah galat per tick, maks 80 px per tick agar tidak overshoot.
    /// </summary>
    public static int Turn(double lx, double lz, double tx, double tz, double pxPerDeg, double gain = 0.5, double deadzone = 2, int max = 80)
    {
        if (Math.Abs(lx) + Math.Abs(lz) < 1e-6 || Math.Abs(tx) + Math.Abs(tz) < 1e-6) return 0;
        // Sudut searah jarum jam dilihat dari atas (Roblox: right = (-lz, lx) untuk look (lx, lz)).
        double cross = lx * tz - lz * tx, dot = lx * tx + lz * tz;
        double deg = Math.Atan2(cross, dot) * 180 / Math.PI; // positif = target di kanan
        if (Math.Abs(deg) < deadzone) return 0;
        // Kendali proporsional (default setengah galat per tick) agar mulus tanpa overshoot.
        return (int)Math.Clamp(Math.Round(deg * gain * Math.Max(0.5, pxPerDeg)), -max, max);
    }

    /// <summary>
    /// Delta Y mouse untuk pitch kamera: mouse ke bawah = kamera menunduk. Target pitch = arah ke musuh, dibatasi -40°..+10°
    /// agar kamera tidak pernah mendongak ke langit-langit.
    /// </summary>
    public static int Pitch(Vec3 look, double dx, double dy, double dz, double pxPerDeg, double gain = 0.5, double deadzone = 3, int max = 60)
    {
        double ll = Math.Sqrt(look.X * look.X + look.Z * look.Z), tl = Math.Sqrt(dx * dx + dz * dz);
        if (ll < 1e-6 && Math.Abs(look.Y) < 1e-6) return 0;
        double cur = Math.Atan2(look.Y, ll) * 180 / Math.PI;
        double want = Math.Clamp(tl < 1e-6 ? -40 : Math.Atan2(dy, tl) * 180 / Math.PI, -40, 10);
        double err = cur - want; // positif = kamera terlalu mendongak → mouse ke bawah
        if (Math.Abs(err) < deadzone) return 0;
        return (int)Math.Clamp(Math.Round(err * gain * Math.Max(0.5, pxPerDeg)), -max, max);
    }

    private static bool IsDash(SkillBinding k) => k.Name == "Dash";
    private static bool IsSwap(SkillBinding k) => k.Name == "Ganti senjata" || k.Name == "Ganti power";

    private bool Ready(string key, long now, int cooldownMs)
    {
        if (_cooldowns.TryGetValue(key, out long last) && now - last < cooldownMs) return false;
        _cooldowns[key] = now;
        return true;
    }

    private readonly List<MacroAction> _pending = [];
    private int _selectedAct;
    private string _selectedMap = "";

    /// <summary>Nomor act dari teks "Act 3", "Training [Act 1]", "Act 1 Training". 0 = tidak ada.</summary>
    public static int ActNumber(string? text)
    {
        if (text is null) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"Act\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>Dipanggil saat menang: catat act yang tuntas (untuk mode "Semua act").</summary>
    public Action<string, int>? ActCleared { get; set; }

    private static bool Matches(string? actual, string want) =>
        actual is not null && want.Length > 0 &&
        actual.Replace(" ", "").Contains(want.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
}

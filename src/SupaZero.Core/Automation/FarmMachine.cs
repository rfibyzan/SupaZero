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
    public int Defeats { get; private set; }
    /// <summary>Map/act yang sedang dimainkan (dari menu Create Room).</summary>
    public string CurrentMap => _selectedMap;
    public int CurrentAct => _selectedAct;
    public int Retries => _retries;
    public string Status { get; private set; } = "Idle";

    public void Reset()
    {
        _pcStep = 0; _pcStepAt = null; _pcOpenAt = null;
        _cooldowns.Clear();
        _roomBan.Clear();
        _pickupBan.Clear();
        _pickupKey = null;
        _path = null;
        _pathGoal = null;
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
        if (screen != _lastScreen)
        {
            if (screen is GameScreen.StartVote || (screen == GameScreen.InRound && _lastScreen != GameScreen.InRound && _roundStart is null)) _roundStart = nowMs;
            int actBefore = _selectedAct;
            OnScreenChanged(_lastScreen, screen);
            if (screen is GameScreen.Victory or GameScreen.Defeat)
            {
                RoundEnded?.Invoke(new Notify.RoundReport(screen == GameScreen.Victory, _selectedMap, actBefore, _lastHud?.Difficulty,
                    _roundStart is long rs ? TimeSpan.FromMilliseconds(nowMs - rs) : null, StagesCleared, s.ResultText, s.Drops));
                _roundStart = null;
            }
        }
        _lastScreen = screen;
        if (s.Hud is not null) _lastHud = s.Hud;

        var actions = screen switch
        {
            GameScreen.Unknown => Wait("Menunggu Roblox / DataModel."),
            GameScreen.Lobby => WalkToLobby(s, S, nowMs),
            GameScreen.LobbyMenu => LobbyMenu(s, S, nowMs),
            GameScreen.QueueWaiting => QueueWait(s, nowMs),
            GameScreen.BlessingPick => PickBlessing(s, S, nowMs),
            GameScreen.TrialsMenu => TrialsMenu(s, S, nowMs),
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
        if (to == GameScreen.Defeat) Defeats++;
        if (to is not (GameScreen.LobbyMenu or GameScreen.TrialsMenu)) { _diffAt = null; _scrolls.Clear(); _targetTries.Clear(); }
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
    /// <summary>Lingkaran Create Room harus benar-benar diinjak (JoinHitbox 18 stud) → radius tetap, tidak ikut "Radius sampai".</summary>
    public const double LobbyArriveRadius = 3;

    /// <summary>
    /// Lobby: lingkaran "Start Here" terdekat ≤ 45 stud → jalan ke sana. Lebih jauh / tidak terbaca → tombol Areas →
    /// kartu teleport (Story) → muncul dekat lingkaran. Posisi rekaman manual hanya cadangan.
    /// </summary>
    private readonly Dictionary<string, long> _roomBan = new(StringComparer.Ordinal);
    private string? _roomTarget;
    private long? _teleportAt;
    private long? _arrivedAt;

    private static string Label(string id) => id.StartsWith("room:") ? "room " + id[5..] : id == "manual" ? "lingkaran rekaman" : "lingkaran";

    private List<MacroAction> WalkToLobby(GameSnapshot s, AppSettings S, long now)
    {
        var f = S.Farm;
        if (!f.AutoCreateLobby) return Wait("Di lobby (Auto create room mati).");
        if (f.CheckPowersInLobby && PowerCheck(s, now) is { } pc) return pc;
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

        // Baru teleport (≤ 30 dtk): spawn bisa agak jauh dari lingkaran → tetap jalan (sampai 90 stud), jangan teleport ulang.
        bool justTeleported = _teleportAt is long ta && now - ta < 30_000;
        double maxWalk = justTeleported ? 90 : TeleportIfFartherThan;

        // Room yang gagal join (sudah sampai tapi menu tidak terbuka) di-skip 30 dtk.
        foreach (var k in _roomBan.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList()) _roomBan.Remove(k);
        var usable = cands.Where(c => !_roomBan.ContainsKey(c.Id)).ToList();
        // Lingkaran dekat semuanya pernah gagal → coba lagi yang dekat, jangan teleport ke area lain (penyebab loop Areas → Teleport).
        if (s.PlayerPos is { } pp)
        {
            var near = cands.Where(c => Navigator.Distance2D(pp.X, pp.Z, c.Pos.X, c.Pos.Z) <= maxWalk).ToList();
            if (near.Count > 0 && !usable.Any(c => near.Contains(c)))
            {
                foreach (var c in near) _roomBan.Remove(c.Id);
                usable = near;
            }
        }
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

        if (target is { } t && dist <= maxWalk)
        {
            _roomTarget = t.Id;
            var walk = WalkTo(s, t.Pos, LobbyArriveRadius, now, "lingkaran Create Room", attackWhenStuck: false);
            if (walk.Count > 0) { _arrivedAt = null; return [.. pre, .. walk]; }

            _arrivedAt ??= now;
            long waited = now - _arrivedAt.Value;
            // Sudah di tepi lingkaran tapi menu belum muncul → masuk ke tengah hitbox.
            if (waited >= 2000 && dist > 2)
            {
                var center = WalkTo(s, t.Pos, 1.5, now, "tengah lingkaran", attackWhenStuck: false);
                if (center.Count > 0) return [.. pre, .. center];
            }
            if (waited >= Math.Max(1, f.JoinTimeoutSec) * 1000L)
            {
                _roomBan[t.Id] = now + 30_000;
                _roomTarget = null;
                _arrivedAt = null;
                Status = "Gagal join, pindah lingkaran.";
                return [new MoveAction([], "Pindah lingkaran"), new NoteAction($"Tidak bisa join {Label(t.Id)} ({waited / 1000} dtk, jarak {dist:0.#} stud) → coba lingkaran lain.")];
            }
            Status = $"Di {Label(t.Id)}, menunggu menu Create Room ({waited / 1000}/{f.JoinTimeoutSec} dtk).";
            return [new MoveAction([], "Sampai")];
        }
        _roomTarget = null;
        _arrivedAt = null;
        if (target is { } far)
            pre.AddRange(Note("room-far", now, 5000, $"{Label(far.Id)} terdekat {dist:0} stud (> {maxWalk:0}) → teleport lewat Areas."));
        else if (s.PlayerPos is null)
            pre.AddRange(Note("lobby-nopos", now, 5000, "Posisi karakter tidak terbaca di lobby → tidak bisa jalan ke lingkaran."));
        else if (cands.Count == 0)
            pre.AddRange(Note("lobby-norooms", now, 5000, "Lingkaran room (QuqueRooms / Start Here) tidak terbaca."));
        if (target is null && s.StartRooms.Count > 0 && s.StartRooms.All(r => r.Occupied) && f.SkipOccupiedRooms)
        {
            Status = "Semua room terisi pemain lain, menunggu yang kosong.";
            return [.. pre, new MoveAction([], "Tunggu room kosong"), .. Note("rooms-full", now, 10000, "Semua room terisi pemain lain, menunggu yang kosong.")];
        }

        // Baru saja teleport: tunggu karakter/lingkaran terbaca, jangan teleport ulang (maks 1x / 12 dtk).
        if (_teleportAt is long tq && now - tq < 12_000)
        {
            Status = "Menunggu setelah teleport…";
            return [.. pre, new MoveAction([], "Tunggu teleport")];
        }

        // Teleport lewat Areas → kartu sesuai mode (Story bila tidak ada kartu bernama mode itu).
        var card = s.TeleportCards.FirstOrDefault(c => Matches(c.Name, f.Mode))
                   ?? s.TeleportCards.FirstOrDefault(c => Matches(c.Name, nameof(FarmMode.Story)));
        if (card?.Rect is { } cr)
        {
            Status = $"Teleport: Areas → {card.Name}";
            if (!Ready("tp-card", now, 2500)) return pre;
            _teleportAt = now;
            return [.. pre, new MoveAction([], "Teleport"), new ClickAction(cr.CenterX, cr.CenterY, $"Teleport {card.Name}")];
        }
        if (s.AreasButton is { } ab)
        {
            Status = "Buka Areas untuk teleport.";
            return Ready("areas", now, 1500) ? [.. pre, new MoveAction([], "Teleport"), new ClickAction(ab.CenterX, ab.CenterY, "Tombol Areas")] : pre;
        }
        Status = "Lingkaran Create Room dan tombol Areas tidak terbaca.";
        return [new MoveAction([], "Tunggu"), .. Note("no-hub", now, 8000, "Tombol Areas / lingkaran 'Start Here' tidak terbaca. Cek di RIE atau rekam posisi lingkaran manual.")];
    }

    // ---------- Cek power + trait (sekali tiap Start) ----------
    private int _pcStep;          // 0 = Trait, 1 = Spin, 2 = selesai
    private long? _pcStepAt, _pcOpenAt;
    public const int PowerMenuReadMs = 3000, PowerMenuTimeoutMs = 15000;

    /// <summary>
    /// Lobby, sebelum Create Room: klik tombol Trait di LobbyHUD → tunggu layar Trait terbaca (3 dtk) → klik lagi (tutup) →
    /// tombol Spin → daftar slot karakter terbaca → tutup. Hanya tombol LobbyHUD yang diklik, tidak ada tombol di dalam menu
    /// (roll/reroll memakai Spins/Rerolls). Tombol tidak terbaca / menu tidak terbuka → langkah dilewati.
    /// </summary>
    private List<MacroAction>? PowerCheck(GameSnapshot s, long now)
    {
        if (_pcStep >= 2) return null;
        Phase = MacroPhase.Lobby;
        string key = _pcStep == 0 ? AnimeZeroPaths.MenuTrait : AnimeZeroPaths.MenuSpin;
        bool open = _pcStep == 0 ? s.TraitMenuOpen : s.SpinMenuOpen;
        GuiRect? btn = s.HubMenuButtons.FirstOrDefault(b => b.Name == key)?.Rect;
        _pcStepAt ??= now;
        var stand = new MoveAction([], "Cek power");

        void Next() { _pcStep++; _pcStepAt = null; _pcOpenAt = null; }

        if (open)
        {
            _pcOpenAt ??= now;
            Status = $"Cek power: menu {key} terbuka, membaca…";
            if (now - _pcOpenAt.Value < PowerMenuReadMs) return [stand];
            var acts = new List<MacroAction> { stand };
            if (btn is { } b && InWindow(s, b.CenterX, b.CenterY)) acts.Add(new ClickAction(b.CenterX, b.CenterY, $"Tutup menu {key}"));
            if (_pcStep == 1 || s.Equipped.Count > 0)
            {
                string eq = s.Equipped.Count == 0 ? "belum terbaca" : string.Join(" · ", s.Equipped.OrderBy(p => p.Loadout).Select(p =>
                    $"slot {p.Loadout} {p.Name}{(p.Level is { } lv ? $" Lv.{lv}" : "")}{(p.Trait is { } t ? $" ({t.Name})" : "")}"));
                acts.AddRange(Note("pc-" + key, now, 1000, $"Cek power ({key}): {eq}."));
            }
            Next();
            return acts;
        }
        if (now - _pcStepAt.Value > PowerMenuTimeoutMs)
        {
            Next();
            return [stand, .. Note("pc-skip-" + key, now, 1000, $"Menu {key} tidak terbuka dalam {PowerMenuTimeoutMs / 1000} dtk → dilewati.")];
        }
        if (btn is { } r)
        {
            Status = $"Cek power: buka menu {key}.";
            if (!InWindow(s, r.CenterX, r.CenterY)) { Next(); return [stand, .. Note("pc-out-" + key, now, 1000, $"Tombol {key} di luar jendela Roblox → dilewati.")]; }
            return Ready("pc-" + key, now, 3000) ? [stand, new ClickAction(r.CenterX, r.CenterY, $"Tombol {key} (LobbyHUD)")] : [stand];
        }
        // Tombol tidak terbaca → langsung lewati (tidak menahan jalan ke room), lanjut langkah berikutnya di tick yang sama.
        if (s.HubMenuButtons.Count > 0 || s.AreasButton is not null)
            _pending.AddRange(Note("pc-nobtn-" + key, now, 60000, $"Tombol {key} di LobbyHUD tidak terbaca → cek {key} dilewati (pakai power tersimpan)."));
        Next();
        return PowerCheck(s, now);
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
        if (DropTarget(s, s.LobbyRewards, s.LobbyRewardsView, f.DropTarget, "lobby-target", now) is { } tgtAct) return tgtAct;
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
        if (s.HudRaw?.Contains("rubble", StringComparison.OrdinalIgnoreCase) == true)
            _pending.AddRange(Note("rubble-diag", now, 8000, s.RubblePos is { } rp
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Rubble di ({rp.X:0},{rp.Y:0},{rp.Z:0}) [{s.RubbleInfo}], pemain {(s.PlayerPos is { } pp ? $"({pp.X:0},{pp.Z:0})" : "?")}.")
                : $"Rubble aktif tapi posisinya tidak terbaca: {s.RubbleInfo}."));
        if (left == 0)
        {
            Phase = MacroPhase.Navigating;
            // Area bersih: pungut koin/health di sekitar dulu (cukup dilewati), baru lanjut ke area berikutnya.
            if (Pickup(s, now, PickupClearRadius, healthOnly: false, S) is { } pick) return pick;
            if (!S.Navigation.AutoNavigate) { Status = $"{area} bersih (Auto navigasi mati)."; return []; }
            if (s.NextAreaPos is null && s.CurrentArea is { } cur && s.PlayerPos is { } pIn && !cur.Contains(pIn, 10))
            {
                var walkIn = WalkPath(s, cur.Center, S.Navigation.ArriveRadius, now, "area pertarungan", S.Navigation.AttackWhenStuck);
                Status = "Jalan masuk area pertarungan.";
                return walkIn.Count > 0 ? walkIn : [new MoveAction([], "Sampai area")];
            }
            if (EnterTarget(s, S, now) is not { } next)
            {
                Status = $"{area} bersih, area berikutnya tidak terbaca.";
                return [new MoveAction([], "Tujuan tidak ada"), .. Note("nav-none", now, 6000, $"{area} bersih tapi posisi area berikutnya (World.Spawns/Areas) tidak terbaca.")];
            }
            var walk = WalkPath(s, next, S.Navigation.ArriveRadius, now, $"area {hud!.TargetArea}", S.Navigation.AttackWhenStuck);
            Status = walk.Count == 0 ? $"Sampai area {hud!.TargetArea}, menunggu musuh." : $"{(hud!.Enter ? $"Masuk area {hud.TargetArea}/{hud.AreaTotal}" : $"{area} bersih")} → jalan ke area {hud.TargetArea}.";
            return walk.Count == 0 ? [new MoveAction([], "Sampai")] : walk;
        }
        Phase = MacroPhase.Fighting;
        Status = $"{Wave(s)}{area} · {(left < 0 ? "?" : left.ToString())} musuh";
        var actions = new List<MacroAction>();
        // Tracking: musuh hidup terdekat → jalan mendekat sampai jarak serang, kursor diarahkan ke musuh.
        // Selama mendekat hanya Dash (Q) yang dipakai; serangan dasar + skill 1/2/3 baru setelah dalam jarak serang.
        bool approaching = false;
        EnemyInfo? tgt = null;
        var candidates = Targets(s);
        // Darah rendah saat bertarung: ambil health terdekat dulu.
        if (s.PlayerHp is { } hp && hp < LowHpPickup && Pickup(s, now, PickupHealthRadius, healthOnly: true, S) is { } heal)
        {
            Status = $"{Status} · darah {hp:P0} → ambil health";
            return heal;
        }
        // Item dekat (≤ 30 stud) didekati sambil tetap menyerang: musuh toh menghampiri pemain. Gagal 6 dtk → dilewati 60 dtk.
        string fightStatus = Status;
        var lootWalk = candidates.Count > 0 ? Pickup(s, now, PickupNearRadius, healthOnly: false, S) : null;
        string? lootStatus = lootWalk is not null ? Status : null;
        Status = fightStatus;
        // Belum ada musuh yang bisa diincar (summon belum muncul, boss IMMUNE): pungut item sambil menunggu.
        if (candidates.Count == 0 && Pickup(s, now, PickupClearRadius, healthOnly: false, S) is { } idlePick) return idlePick;
        if (s.PlayerPos is { } meIn && candidates.Count == 0)
        {
            // Hanya boss IMMUNE yang tersisa / musuh belum muncul: masuk ke area (mis. naik tangga dari spawn raid).
            if (s.CurrentArea is { } box && !box.Contains(meIn, 10))
            {
                Phase = MacroPhase.Navigating;
                var walkIn = WalkPath(s, box.Center, S.Navigation.ArriveRadius, now, "area pertarungan", S.Navigation.AttackWhenStuck);
                Status = s.BossImmune ? $"Boss {s.BossName} IMMUNE → masuk area dulu." : "Jalan masuk area pertarungan.";
                return walkIn.Count > 0 ? walkIn : [new MoveAction([], "Sampai area")];
            }
            if (s.BossImmune)
            {
                Status = $"{Wave(s)}Boss {s.BossName} IMMUNE, menunggu summon.";
                return Ready("jump-wait", now, 1100) ? [new MoveAction([], "Tunggu summon"), new KeyAction(VirtualKeys.Space, 80, "Lompat (menunggu summon)")] : [new MoveAction([], "Tunggu summon")];
            }
        }
        if (s.PlayerPos is { } me && candidates
                .OrderBy(e => Navigator.Distance2D(me.X, me.Z, e.Pos.X, e.Pos.Z)).FirstOrDefault() is { } target)
        {
            tgt = target;
            double d = Navigator.Distance2D(me.X, me.Z, target.Pos.X, target.Pos.Z);
            Status += $" · target #{target.Name} {d:0} stud";
            if (S.Navigation.ChaseEnemies)
            {
                // Jarak serang dipertahankan: lebih jauh dari jarak + toleransi → kejar; dalam pita → roaming di lingkaran berjari-jari jarak serang
                // (terlalu dekat otomatis mundur karena titik roaming ada di lingkaran itu).
                bool isRubble = target.Name == AnimeZeroPaths.RubbleName;
                // Rubble = dinding solid: cukup dekat (≤ 12 stud) lalu serang; jalan lurus tanpa A* (titiknya di dalam dinding).
                double range = isRubble ? Math.Min(12, S.Navigation.AttackRange) : S.Navigation.AttackRange;
                approaching = lootWalk is null && d > range + (isRubble ? 2 : RangeTolerance(S.Navigation.AttackRange));
                if (isRubble) Status += $" · rubble {target.Health:P0}";
                if (lootWalk is not null) { actions.AddRange(lootWalk); Status += $" · {lootStatus}"; }
                var walk = lootWalk is not null ? [] : approaching ? WalkPath(s, target.Pos, range, now, isRubble ? "rubble" : $"musuh #{target.Name}", isRubble) : [];
                if (lootWalk is null) actions.AddRange(walk.Count > 0 ? walk : [new MoveAction([], "Dekat musuh")]);
            }
            // Bidik ke tengah kelompok: rata-rata posisi musuh hidup ≤ 15 stud dari target (skill kebanyakan AOE).
            var group = candidates.Where(e => Navigator.Distance2D(e.Pos.X, e.Pos.Z, target.Pos.X, target.Pos.Z) <= ClusterRadius).ToList();
            var aimPos = group.Count > 1
                ? new Vec3(group.Average(e => e.Pos.X), group.Average(e => e.Pos.Y), group.Average(e => e.Pos.Z))
                : target.Pos;
            string aimLabel = group.Count > 1 ? $"tengah {group.Count} musuh" : $"musuh #{target.Name}";
            if (group.Count > 1) Status += $" · bidik {group.Count} musuh";
            if (S.Navigation.AimAtEnemy && s.ShiftLock)
            {
                // Shift lock: kursor terkunci di tengah → putar kamera (yaw) ke arah musuh dengan gerak relatif.
                // Yaw dan pitch dihitung dari posisi kamera (shift lock menggeser kamera ke kanan bahu); pitch dijaga -80°..+10°.
                int dx = 0, dy = 0;
                if (s.Camera is { } cam)
                {
                    dx = Turn(cam.Look.X, cam.Look.Z, aimPos.X - cam.Pos.X, aimPos.Z - cam.Pos.Z, S.Navigation.TurnPixelsPerDegree);
                    dy = Pitch(cam.Look, aimPos.X - cam.Pos.X, aimPos.Y - cam.Pos.Y, aimPos.Z - cam.Pos.Z, S.Navigation.TurnPixelsPerDegree);
                }
                else if (s.CameraLook is { } cl)
                    dx = Turn(cl.X, cl.Z, aimPos.X - me.X, aimPos.Z - me.Z, S.Navigation.TurnPixelsPerDegree);
                if (dx != 0 || dy != 0)
                    actions.Add(new TurnAction(dx, dy, $"Putar kamera ke {aimLabel}"));
            }
            else if (S.Navigation.AimAtEnemy && s.Camera is { } cam2 && Ready("cam-drag", now, 700))
            {
                // Tanpa shift lock: skill mengikuti kursor, jadi kamera ikut diputar (klik kanan tahan + geser)
                // bila musuh melenceng > 20° dari tengah atau pitch keluar batas -80°..+10°.
                double ex = aimPos.X - cam2.Pos.X, ey = aimPos.Y - cam2.Pos.Y, ez = aimPos.Z - cam2.Pos.Z;
                int tdx = Turn(cam2.Look.X, cam2.Look.Z, ex, ez, S.Navigation.TurnPixelsPerDegree, gain: 1, deadzone: 20, max: 200);
                int tdy = Pitch(cam2.Look, ex, ey, ez, S.Navigation.TurnPixelsPerDegree, gain: 1, deadzone: 8, max: 150);
                if (tdx != 0 || tdy != 0)
                    actions.Add(new TurnAction(tdx, tdy, $"Putar kamera (klik kanan) ke {aimLabel}", Drag: true));
                else _cooldowns.Remove("cam-drag");
            }
            if (S.Navigation.AimAtEnemy && !s.ShiftLock && s.Camera?.WorldToScreen(aimPos) is { } sp
                && sp.X >= 0 && sp.Y >= 0 && sp.X < s.Camera.ViewW && sp.Y < s.Camera.ViewH)
                actions.Add(new AimAction(sp.X, sp.Y, $"Arahkan ke {aimLabel}"));
        }

        // Tidak ada target (hitungan enemies > 0 tapi model belum punya HumanoidRootPart/HP): jangan serang udara.
        // HUD "Enter area N" → jalan ke area itu; selain itu tunggu.
        if (tgt is null && hud is { Enter: true } && EnterTarget(s, S, now) is { } enterPos && S.Navigation.AutoNavigate)
        {
            Phase = MacroPhase.Navigating;
            var go = WalkPath(s, enterPos, S.Navigation.ArriveRadius, now, $"area {hud.TargetArea}", S.Navigation.AttackWhenStuck);
            Status = $"Masuk area {hud.TargetArea}/{hud.AreaTotal} (musuh belum terbaca).";
            return go.Count > 0 ? go : [new MoveAction([], "Sampai")];
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
            _lastSkillPress = now;
            _attackHoldUntil = now + 250;
            // Saat animasi skill berjalan, Space ditolak game → lompat dulu (sambil roaming), lalu skill dilepas di udara.
            if (!approaching && S.Navigation.ChaseEnemies && now - _lastJump >= 600)
            {
                _lastJump = now;
                _nextRoamJump = now + 700 + _rng.Next(600);
                actions.Add(new KeyAction(VirtualKeys.Space, 90, "Lompat (sebelum skill)"));
            }
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
        // Skill 1/2/3 habis (cooldown semua / tidak ada yang siap): Dash (Q) searah roaming agar tidak diam jadi sasaran.
        bool skillsOut = s.SkillCooldowns.Count > 0 ? s.SkillCooldowns.All(c => !c.Ready) : due is null;
        // Jangan Dash ≤ 500 ms sebelum T ditekan (animasi Dash bisa menelan T) atau di tick yang sama.
        bool swapSoon = _swapPrepSince is long ps && now >= _swapRestUntil && Math.Max(ps + SwapPrepMs, _swapLastPress + 1200) - now < 500;
        bool swapNow = swap is not null && actions.Any(a => a is KeyAction k && k.Reason.StartsWith(swap.Name, StringComparison.Ordinal));
        // Juga di sela skill: setelah animasi skill selesai dan tidak ada skill ditekan tick ini → Dash ke samping (arah roaming).
        bool skillGap = due is null && now - _lastSkillPress >= SkillAnimMs && now - _lastJump >= 250;
        if ((skillsOut || skillGap) && dash is not null && !s.DashCooling && !swapSoon && !swapNow
            && actions.OfType<MoveAction>().Any(m => m.Keys.Count > 0) && Ready("dash-roam", now, Math.Max(1500, dash.CooldownMs))
            && HotkeyMap.TryGetVirtualKey(dash.Key, out uint rvk))
            actions.Add(new KeyAction((ushort)rvk, 40, $"{dash.Name} ({dash.Key}) menghindar{(skillsOut ? ", skill habis" : " di sela skill")}"));
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
        double r = Math.Max(2.5, S.Navigation.AttackRange);
        if (_roamSince is not long t0 || now - _roamLast > 1500) { t0 = now; _roamSince = now; _roamAngle = Math.Atan2(p.Z - target.Pos.Z, p.X - target.Pos.X); }
        _roamLast = now;
        // Arah putar kanan/kiri berganti acak tiap 2,5–4,5 dtk.
        if (now >= _roamFlipAt)
        {
            if (_roamFlipAt != 0) { _roamAngle += _roamDir * (now - t0) / 1000.0 * Math.Min(80 * Math.PI / 180, 12.0 / r); _roamSince = t0 = now; _roamDir = -_roamDir; }
            _roamFlipAt = now + 2500 + _rng.Next(2000);
        }
        // Kecepatan sudut diturunkan untuk lingkaran besar (± 12 stud/dtk di keliling); titik tuju 25° di depan agar jalur tetap dekat lingkaran.
        double omega = Math.Min(80 * Math.PI / 180, 12.0 / r);
        double a = _roamAngle + _roamDir * (Math.PI * 25 / 180 + (now - t0) / 1000.0 * omega);
        // Koreksi radial: terlalu dekat → titik tuju digeser keluar, terlalu jauh → ke dalam, agar jarak kembali ke r.
        double cur = Navigator.Distance2D(p.X, p.Z, target.Pos.X, target.Pos.Z);
        r = Math.Max(2.5, r + Math.Clamp(r - cur, -r * 0.5, r * 0.5));
        double gx = target.Pos.X + r * Math.Cos(a), gz = target.Pos.Z + r * Math.Sin(a);
        var keys = Navigator.KeysToward(p.X, p.Z, look.X, look.Z, gx, gz, 0.8);
        actions.RemoveAll(x => x is MoveAction);
        actions.Insert(0, new MoveAction(keys, "Roaming melingkari musuh"));
        // Lompat terjadwal ditunda bila skill baru ditekan (< 700 ms, animasi skill menelan Space) atau sudah lompat sebelum skill.
        if (now >= _nextRoamJump && now - _lastSkillPress >= SkillAnimMs && now - _lastJump >= 600)
        {
            _nextRoamJump = now + 700 + _rng.Next(600);
            _lastJump = now;
            // Lompat ditolak game selama animasi serang: buang klik serangan di tick ini dan tahan serangan 300 ms.
            actions.RemoveAll(x => x is AttackAction);
            _attackHoldUntil = Math.Max(_attackHoldUntil, now + 300);
            actions.Add(new KeyAction(VirtualKeys.Space, 100, "Lompat (roaming)"));
        }
    }
    /// <summary>Pita toleransi jarak serang (stud): maks(2, 10%).</summary>
    public static double RangeTolerance(double range) => Math.Max(2, range * 0.1);
    private long _attackHoldUntil, _swapLastPress = long.MinValue / 2, _swapRestUntil;
    private long? _swapPrepSince;
    private int _swapTries;
    /// <summary>Lama berhenti basic attack sebelum T agar animasi serangan selesai.</summary>
    private const int SwapPrepMs = 900;
    private bool ResetSwap() { _swapPrepSince = null; _swapTries = 0; return true; }
    private long? _roamSince;
    private long _roamLast, _nextRoamJump, _roamFlipAt, _lastJump = long.MinValue / 2, _lastSkillPress = long.MinValue / 2;
    /// <summary>Perkiraan lama animasi skill (Space/Dash ditolak game selama itu).</summary>
    private const int SkillAnimMs = 700;
    private int _roamDir = 1;
    private double _roamAngle;
    private readonly Random _rng = new();

    /// <summary>Jalan ke tujuan dengan W/A/S/D; tersangkut ≥1,5 dtk → lompat (+ serang bila diizinkan, untuk rubble).</summary>
    // ---------- Item di lantai (Workspace.PooledDrops) ----------

    /// <summary>Radius pungut koin/health saat area bersih; radius health saat darah rendah; ambang darah rendah.</summary>
    internal const double PickupClearRadius = 90, PickupHealthRadius = 60, PickupNearRadius = 30, ClusterRadius = 15, LowHpPickup = 0.5;
    /// <summary>Item yang tidak terambil dalam waktu ini (terhalang/tidak terjangkau) dilewati.</summary>
    internal const long PickupGiveUpMs = 6000;
    private readonly Dictionary<string, long> _pickupBan = new(StringComparer.Ordinal);
    private string? _pickupKey;
    private long _pickupSince;

    private static string PickupKey(PickupInfo p) => $"{p.Kind}@{Math.Round(p.Pos.X)},{Math.Round(p.Pos.Z)}";

    /// <summary>Jalan melewati item terdekat (koin/health) dalam radius. null = tidak ada item yang perlu diambil.</summary>
    private List<MacroAction>? Pickup(GameSnapshot s, long now, double radius, bool healthOnly, AppSettings S)
    {
        if (s.PlayerPos is not { } me || s.Pickups.Count == 0) { _pickupKey = null; return null; }
        // Health hanya dipungut bila darah tidak penuh (pickup terbuang bila penuh).
        bool wantHealth = s.PlayerHp is not { } hpNow || hpNow < 0.95;
        var best = s.Pickups
            .Where(p => (healthOnly ? p.IsHealth : (!p.IsHealth || wantHealth)) && !(_pickupBan.TryGetValue(PickupKey(p), out long until) && until > now))
            .Select(p => (p, d: Navigator.Distance2D(me.X, me.Z, p.Pos.X, p.Pos.Z)))
            .Where(t => t.d <= radius && Math.Abs(t.p.Pos.Y - me.Y) <= 25)
            .OrderBy(t => t.d).FirstOrDefault();
        if (best.p is null) { _pickupKey = null; return null; }
        string key = PickupKey(best.p);
        if (_pickupKey != key) { _pickupKey = key; _pickupSince = now; }
        else if (now - _pickupSince > PickupGiveUpMs)
        {
            _pickupBan[key] = now + 60_000;
            _pickupKey = null;
            return [new MoveAction([], "Lewati item"), .. Note("pickup-ban", now, 4000, $"Item {best.p.Kind} tidak terambil dalam {PickupGiveUpMs / 1000} dtk, dilewati.")];
        }
        string what = best.p.IsHealth ? "health" : "koin";
        // Radius 2 (bukan 1): item terambil saat tersentuh; radius 1 bikin karakter maju-mundur pelan di atas item.
        var walk = WalkPath(s, best.p.Pos, 2.0, now, what, false);
        int left = s.Pickups.Count;
        Status = $"Ambil {what} ({best.d:0} stud, {left} item)";
        return walk.Count > 0 ? walk : [new MoveAction([], $"Di atas {what}")];
    }

    // ---------- Cari jalur (A* di peta Areas + rintangan Navigation) ----------

    private readonly PathPlanner _planner = new();
    private List<Vec3>? _path;
    private Vec3? _pathGoal;
    private long _pathAt;
    private int _enterArea, _enterDepth;
    private long _enterNearSince;

    /// <summary>
    /// Tujuan "Enter area N". Bila sudah sampai di titik masuk tapi HUD masih "Enter area N" (pemicu game ada lebih dalam,
    /// mis. Hxh Area 2 berhenti ~31 stud dari penanda), maju bertahap 15 stud tiap 1,5 dtk ke pusat kotak Areas.N.
    /// </summary>
    private Settings.Vec3? EnterTarget(GameSnapshot s, AppSettings S, long now)
    {
        if (s.NextAreaPos is not { } baseP) return null;
        var hud = s.Hud;
        int n = hud?.TargetArea ?? 0;
        if (n != _enterArea || hud is not { Enter: true }) { _enterArea = n; _enterDepth = 0; _enterNearSince = 0; }
        Settings.Vec3 Target()
        {
            if (_enterDepth == 0 || s.NextAreaCenter is not { } c) return baseP;
            double dx = c.X - baseP.X, dz = c.Z - baseP.Z, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1) return baseP;
            double t = Math.Min(len, _enterDepth * 15.0) / len;
            return new(baseP.X + dx * t, baseP.Y, baseP.Z + dz * t);
        }
        var tgt = Target();
        if (hud is { Enter: true } && s.PlayerPos is { } p)
        {
            double d = Math.Sqrt((p.X - tgt.X) * (p.X - tgt.X) + (p.Z - tgt.Z) * (p.Z - tgt.Z));
            if (d <= S.Navigation.ArriveRadius + 3)
            {
                if (_enterNearSince == 0) _enterNearSince = now;
                else if (now - _enterNearSince >= 1500) { _enterDepth++; _enterNearSince = 0; _path = null; tgt = Target(); }
            }
            else _enterNearSince = 0;
        }
        return tgt;
    }
    private string? _pathNote;

    /// <summary>
    /// Jalan ke tujuan lewat jalur, bukan garis lurus: bila garis lurus terhalang rintangan atau keluar area, A* mencari rute
    /// (mis. memutar huruf U), lalu karakter berjalan dari titik belok ke titik belok. Tanpa peta / jalur → <see cref="WalkTo"/>.
    /// </summary>
    private List<MacroAction> WalkPath(GameSnapshot s, Vec3 target, double radius, long now, string label, bool attackWhenStuck)
    {
        if (s.Nav is not { IsEmpty: false } nav || s.PlayerPos is not { } p
            || Navigator.Distance2D(p.X, p.Z, target.X, target.Z) <= Math.Max(radius, 6))
        { _path = null; return WalkTo(s, target, radius, now, label, attackWhenStuck); }
        bool stuck = _stuckRef is not null && now - _stuckSince >= 1500;
        bool goalMoved = _pathGoal is not { } g || Navigator.Distance2D(g.X, g.Z, target.X, target.Z) > 6;
        // Tersangkut di objek yang tidak ada di peta: tandai titik ~2,5 stud di depan (arah titik belok) sebagai rintangan lalu cari ulang.
        if (stuck && _path is { Count: > 0 } cur && now - _pathAt > 1500)
        {
            var w0 = cur[0];
            double d = Math.Max(0.01, Navigator.Distance2D(p.X, p.Z, w0.X, w0.Z));
            _planner.Block(p.X + (w0.X - p.X) / d * 2.5, p.Z + (w0.Z - p.Z) / d * 2.5);
        }
        if (_path is null || goalMoved || stuck && now - _pathAt > 1500 || now - _pathAt > 4000)
        {
            _pathGoal = target;
            _pathAt = now;
            bool clear = _planner.Clear(nav, p, target);
            _path = clear ? [target] : _planner.Find(nav, p, target);
            // Diagnosa (maks 1x / 10 dtk): posisi, tujuan, jenis jalur, jumlah rintangan peta → bisa dicocokkan dengan snapshot.
            _pending.AddRange(Note("path-diag", now, 10000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Jalur ke {label}: dari ({p.X:0},{p.Z:0}) ke ({target.X:0},{target.Z:0}), {(clear ? "lurus" : _path is null ? "tidak ketemu" : $"{_path.Count} titik{(_planner.UsedOutside ? ", lewat luar Areas" : "")}{(_planner.Partial ? ", parsial" : "")}")}; peta {nav.Walkable.Count} area, {nav.Solids?.Count ?? 0} rintangan.")));
            if (_path is null)
            {
                return [.. WalkTo(s, target, radius, now, label, attackWhenStuck), .. Note("path-none", now, 8000, $"Jalur ke {label} tidak ketemu: {_planner.LastFailure ?? "?"}. Jalan lurus.")];
            }
            if (_planner.Partial) _pathNote = $"Jalur ke {label}: {_planner.LastFailure}.";
            else if (_planner.UsedOutside && _path.Count > 1) _pathNote = $"Jalur ke {label} memutar lewat jalan di luar kotak Areas ({_path.Count} titik belok).";
        }
        // Buang titik belok yang sudah dilewati (≤ 3 stud) atau yang sudah bisa dilihat titik sesudahnya.
        while (_path.Count > 1 && Navigator.Distance2D(p.X, p.Z, _path[0].X, _path[0].Z) <= 3) _path.RemoveAt(0);
        var wp = _path[0];
        bool last = _path.Count == 1;
        var walk = WalkTo(s, wp, last ? radius : 1.5, now, last ? label : $"{label} (belok {_path.Count - 1})", attackWhenStuck);
        // Jalur parsial sudah sampai ujung: sisanya jalan lurus (mis. hancurkan rubble/Blocker dengan serang saat tersangkut).
        if (walk.Count == 0 && last && _planner.Partial) return WalkTo(s, target, radius, now, label, attackWhenStuck);
        if (_pathNote is { } pn) { _pathNote = null; walk = [.. walk, .. Note("path-partial", now, 8000, pn)]; }
        if (walk.Count == 0 && !last) { _path.RemoveAt(0); return WalkPath(s, target, radius, now, label, attackWhenStuck); }
        if (walk.Count == 0) _path = null;
        return walk;
    }

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

    // ---------- Mode non-Story ----------

    private readonly Dictionary<string, int> _targetTries = new(StringComparer.Ordinal);

    private static string Wave(GameSnapshot s) => s.RunWave is { Length: > 0 } w ? w + " · " : "";

    /// <summary>Musuh yang boleh diserang: boss IMMUNE (berdiri di Markers.BossSpawn) dilewati selama masih ada musuh lain.</summary>
    public static List<EnemyInfo> Targets(GameSnapshot s)
    {
        var alive = s.Enemies.Where(e => e.Alive).ToList();
        if (!s.BossImmune || s.BossSpawn is not { } bs) return alive;
        return alive.Where(e => Navigator.Distance2D(e.Pos.X, e.Pos.Z, bs.X, bs.Z) > BossSpawnRadius).ToList();
    }

    /// <summary>Boss IMMUNE dianggap yang berada sejauh ini dari Markers.BossSpawn (snapshot: tepat di titiknya).</summary>
    public const double BossSpawnRadius = 12;

    /// <summary>Klik kartu reward <paramref name="want"/> sampai bertanda UNTARGET (= ditarget). null = sudah / tidak berlaku.</summary>
    private List<MacroAction>? DropTarget(GameSnapshot s, IReadOnlyList<RewardCard> cards, GuiRect? view, string want, string key, long now)
    {
        if (string.IsNullOrWhiteSpace(want) || cards.Count == 0) return null;
        var c = cards.FirstOrDefault(x => Matches(x.Name, want.Trim()));
        if (c is null)
        {
            _pending.AddRange(Note(key + "-none", now, 30000, $"Drop Target '{want}' tidak ada di Rewards (ada: {string.Join(", ", cards.Where(x => x.Targetable).Select(x => x.Name))})."));
            return null;
        }
        if (!c.Targetable || c.Targeted || c.Hit is null) { if (c.Targeted) _targetTries.Remove(key); return null; }
        int n = _targetTries.GetValueOrDefault(key);
        if (n >= 3)
        {
            _pending.AddRange(Note(key + "-fail", now, 30000, $"Drop Target {c.Name} belum terbaca ditarget setelah 3 klik → lanjut Start."));
            return null;
        }
        var act = ClickEntry(s, new LobbyEntry(c.Name, false, false, c.Hit), view, key, $"Drop Target {c.Name}", now);
        if (act is { Count: > 0 } && act[0] is ClickAction) { _targetTries[key] = n + 1; Status = $"Drop Target: {c.Name}."; }
        return act ?? [];
    }

    /// <summary>Pilih kartu Mirror Blessing: urutan nama di pengaturan, lalu rarity tertinggi.</summary>
    private List<MacroAction> PickBlessing(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Fighting;
        var prio = S.Farm.BlessingPriority.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pick = ChooseBlessing(s.Blessings, prio);
        if (pick?.Pick is not { } r) return Note("bless-none", now, 5000, "Kartu Mirror Blessing terbaca tapi tombol PICK tidak.");
        Status = $"Mirror Blessing: {pick.Name} ({pick.Rarity}) dari {string.Join(" / ", s.Blessings.Select(b => b.Name))}";
        if (!InWindow(s, r.CenterX, r.CenterY)) return Note("bless-out", now, 5000, $"Tombol PICK {pick.Name} di luar jendela.");
        return Ready("bless-pick", now, 1500) ? [new MoveAction([], "Pilih kartu"), new ClickAction(r.CenterX, r.CenterY, $"PICK {pick.Name}")] : [];
    }

    public static BlessingOffer? ChooseBlessing(IReadOnlyList<BlessingOffer> offers, IReadOnlyList<string> priority)
    {
        foreach (var want in priority)
            if (offers.FirstOrDefault(o => Matches(o.Name, want)) is { } hit) return hit;
        return offers.OrderByDescending(o => RarityRank(o.Rarity)).FirstOrDefault();
    }

    private static int RarityRank(string r) => r.ToUpperInvariant() switch
    {
        "COMMON" => 1, "RARE" => 2, "EPIC" => 3, "LEGENDARY" => 4, "MYTHIC" => 5, _ => 0,
    };

    /// <summary>Panel Trials: buka tab Floor Rewards → atur floor (bila diset) → Drop Target → Start. Skip floor berbayar tidak pernah diklik.</summary>
    private List<MacroAction> TrialsMenu(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Lobby;
        var t = s.Trials!;
        var f = S.Farm;
        if (!f.AutoCreateLobby) return Wait("Panel Trials terbuka (Auto create room mati).");
        if (!t.FloorTabOpen)
        {
            if (t.FloorTab is not { } tab) return Note("tr-tab", now, 5000, "Tab Floor Rewards Trials tidak terbaca.");
            Status = "Trials: buka tab Floor Rewards.";
            return Ready("tr-tab", now, 1500) && InWindow(s, tab.CenterX, tab.CenterY) ? [new ClickAction(tab.CenterX, tab.CenterY, "Tab Floor Rewards")] : [];
        }
        if (f.TrialsFloor > 0 && t.SelectedFloor is int cur && cur != f.TrialsFloor)
        {
            var btn = cur < f.TrialsFloor ? t.Increase : t.Decrease;
            Status = $"Trials: floor {cur} → {f.TrialsFloor}.";
            if (btn is not { } b) return Note("tr-floor", now, 5000, "Tombol +/- floor Trials tidak terbaca.");
            if (cur < f.TrialsFloor && t.HighestFloor is int hi && cur > hi)
                return Stop($"Floor {f.TrialsFloor} belum terbuka (tertinggi {hi}).");
            return Ready("tr-floor", now, 350) ? [new ClickAction(b.CenterX, b.CenterY, cur < f.TrialsFloor ? "Floor +" : "Floor -")] : [];
        }
        if (DropTarget(s, t.Rewards, t.RewardsView, f.DropTarget, "trials-target", now) is { } tg) return tg;
        _selectedMap = "Trials";
        _selectedAct = t.SelectedFloor ?? 0;
        if (t.Start is not { } st) return Note("tr-start", now, 5000, "Tombol Start Trials tidak terbaca.");
        Status = $"Trials floor {t.SelectedFloor?.ToString() ?? "?"} → Start.";
        return Ready("tr-start", now, 3000) && InWindow(s, st.CenterX, st.CenterY) ? [new ClickAction(st.CenterX, st.CenterY, "Start Trials")] : [];
    }

    // ---------- Layar akhir ----------

    private List<MacroAction> Victory(GameSnapshot s, AppSettings S, long now)
    {
        var up = S.Upgrade;
        if (up.Enabled && !_upgradeDoneThisStage && Upgrade(s, up, now) is { } upgrade) return upgrade;

        var after = S.Farm.AfterWin == AfterWin.Auto
            ? (Matches(S.Farm.Mode, nameof(FarmMode.Story)) || Matches(S.Farm.Mode, nameof(FarmMode.Trials)) ? AfterWin.Next : AfterWin.Retry)
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
        double want = Math.Clamp(tl < 1e-6 ? -80 : Math.Atan2(dy, tl) * 180 / Math.PI, -80, 10); // -80°: musuh menggerombol di bawah saat pemain di udara
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
    private long? _roundStart;
    private HudInfo? _lastHud;
    /// <summary>Dipanggil sekali saat layar berubah ke Victory/Defeat (untuk webhook).</summary>
    public Action<Notify.RoundReport>? RoundEnded { get; set; }

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

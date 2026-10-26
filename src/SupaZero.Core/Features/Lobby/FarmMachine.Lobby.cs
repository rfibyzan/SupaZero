using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
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
    private long? _areasClickAt;      // v0.3.8: kartu teleport hanya diklik sesudah tombol Areas diklik (≤ 6 dtk)
    private Vec3? _tpFrom;            // posisi saat kartu teleport diklik (cek teleport benar terjadi)
    private long? _arrivedAt;
    /// <summary>v0.3.1 (screenshot Byzan 08:24, 25 Okt): karakter sudah di lingkaran tapi jarak terbaca tetap ~11 stud, menu tidak terbuka,
    /// macro terus "jalan" → tidak pernah masuk hitungan JoinTimeout. Jarak tidak membaik ≥ 1 stud selama ini → anggap gagal, pindah lingkaran.</summary>
    private string? _progTarget;
    private double _progBest;
    private long _progBestAt;

    /// <summary>v0.3.8: batas tertahan = setelan "Pindah lingkaran jika gagal join" (dtk), bukan 6 dtk tetap.</summary>
    private bool RoomStalled(string id, double dist, long now, long limitMs)
    {
        if (_progTarget != id) { _progTarget = id; _progBest = dist; _progBestAt = now; return false; }
        if (dist < _progBest - 1) { _progBest = dist; _progBestAt = now; return false; }
        return now - _progBestAt >= limitMs;
    }

    private List<MacroAction> BanRoom(string id, long now, string why)
    {
        _roomBan[id] = now + 30_000;
        _roomTarget = null;
        _arrivedAt = null;
        _progTarget = null;
        Status = "Gagal join, pindah lingkaran.";
        return [new MoveAction([], "Pindah lingkaran"), new NoteAction($"Tidak bisa join {Label(id)} ({why}) → coba lingkaran lain.")];
    }

    private static string Label(string id) => id.StartsWith("room:") ? "room " + id[5..] : id == "manual" ? "lingkaran rekaman" : "lingkaran";

    private List<MacroAction> WalkToLobby(GameSnapshot s, AppSettings S, long now, bool force = false, string? cardName = null)
    {
        var f = S.Farm;
        // v0.3.2 (Byzan 25 Okt 08:30): semua lingkaran Create Room ada di area Story → teleport selalu lewat kartu "Story"
        // (MATCHES_Row1.Story.Story), apa pun mode farm (Raid/Infinite/...). Mode dipilih nanti di menu Create Room.
        string want = cardName ?? CalRoomCard;
        if (!force && !f.AutoCreateLobby) return Wait("Di lobby (Auto create room mati).");
        if (!force && S.Upgrade.Enabled && !_upgradeDoneThisStage && LobbyUpgrade(s, S.Upgrade, now) is { } lu) return lu;
        if (!force && f.CheckPowersInLobby && PowerCheck(s, now) is { } pc) return pc;
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
        // v0.3.8 (Byzan 25 Okt 10:18): sesudah teleport, lingkaran yang posisinya terbaca dari memori didatangi berapa pun jaraknya
        // (dulu dibatasi 90 stud → diam lalu teleport ulang terus).
        bool justTeleported = _teleportAt is long ta && now - ta < 30_000;
        double maxWalk = justTeleported ? double.MaxValue : TeleportIfFartherThan;
        long joinMs = Math.Max(1, f.JoinTimeoutSec) * 1000L;

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
            if (walk.Count > 0)
            {
                _arrivedAt = null;
                // Lingkaran = pembatas (tidak bisa dimasuki): jarak berhenti turun + menu tidak terbuka selama setelan join → pindah.
                if (RoomStalled(t.Id, dist, now, joinMs)) return [.. pre, .. BanRoom(t.Id, now, $"jarak tertahan {dist:0.#} stud {joinMs / 1000} dtk, menu tidak terbuka")];
                return [.. pre, .. walk];
            }

            _arrivedAt ??= now;
            long waited = now - _arrivedAt.Value;
            if (waited >= joinMs)
                return BanRoom(t.Id, now, $"{waited / 1000} dtk, jarak {dist:0.#} stud");
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
        // v0.3.8 (Byzan 25 Okt 10:20 "belum buka tab teleport"): kartu bisa terbaca walau panel Areas tertutup → klik kosong.
        // Bukti teleport = posisi berubah. 3 dtk tanpa pindah ≥ 20 stud → anggap gagal, buka Areas dulu.
        if (_teleportAt is long tf && now - tf >= 3000 && _tpFrom is { } from && s.PlayerPos is { } pnow
            && Navigator.Distance2D(pnow.X, pnow.Z, from.X, from.Z) < 20)
        {
            _teleportAt = null; _tpFrom = null; _areasClickAt = null;
            pre.Add(new NoteAction("Teleport tidak terjadi (posisi tidak berubah 3 dtk) → buka Areas dulu lalu klik kartu lagi."));
        }
        else if (_teleportAt is long tq && now - tq < 12_000)
        {
            Status = "Menunggu setelah teleport…";
            string why = s.PlayerPos is null ? "posisi karakter tidak terbaca"
                : cands.Count == 0 ? "lingkaran room (QuqueRooms / Start Here) belum terbaca"
                : "semua lingkaran dekat sedang di-skip";
            return [.. pre, new MoveAction([], "Tunggu teleport"), .. Note("tp-wait", now, 4000, $"Menunggu setelah teleport: {why}.")];
        }

        // Teleport lewat Areas → kartu bernama mode itu. v0.2.80: tanpa cadangan kartu Story.
        var card = s.TeleportCards.FirstOrDefault(c => c.Name.Trim().Equals(want, StringComparison.OrdinalIgnoreCase));
        if (card is null && s.TeleportCards.Count > 0)
            return [.. pre, new MoveAction([], "Tunggu"), .. Note("tp-nocard", now, 8000, $"Kartu teleport '{want}' tidak ada (terbaca: {string.Join(", ", s.TeleportCards.Select(c => c.Name))}) → tidak diklik.")];
        bool areasJustClicked = _areasClickAt is long ac && now - ac <= 6000;
        if (card?.Rect is not null && (areasJustClicked || s.AreasButton is null))
        {
            Status = $"Teleport: Areas → {card.Name}";
            // v0.2.90: klik hanya bila kartu terlihat di dalam panel Areas (Clipping) dan jendela Roblox; selain itu scroll/diam.
            var click = ClickEntry(s, card, s.AreasView, "tp-card", $"Teleport {card.Name}", now) ?? [];
            if (click.Any(a => a is ClickAction)) { _teleportAt = now; _tpFrom = s.PlayerPos; _areasClickAt = null; }
            return [.. pre, new MoveAction([], "Teleport"), .. click];
        }
        if (s.AreasButton is { } ab)
        {
            Status = "Buka Areas untuk teleport.";
            if (areasJustClicked) return [.. pre, new MoveAction([], "Tunggu panel Areas")];
            if (!Ready("areas", now, 1500)) return pre;
            _areasClickAt = now;
            return [.. pre, new MoveAction([], "Teleport"), new ClickAction(ab.CenterX, ab.CenterY, "Tombol Areas")];
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
        // v0.2.80 (jangan menebak): act yang diminta tidak ada → berhenti, bukan pindah ke act lain.
        if (acts.Count > 0 && !acts.Any(a => a.No == want))
            return Stop($"Act {want} tidak ada di map '{mapKey}' (tersedia: {string.Join(", ", acts.Select(a => a.No))}). Ubah Act di kartu Farm.");
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
        // v0.2.80: difficulty terpilih wajib terbaca (dulu: tag kosong → klik tiap 8 dtk tanpa tahu hasilnya).
        if (!diffKnown) return Note("lobby-diff-unknown", now, 5000, "Difficulty terpilih tidak terbaca → Start ditahan.");
        if (!diffOk)
        {
            if (DifficultyPoint(s, f.Difficulty) is not var (dx, dy))
                return Note("lobby-diff", now, 5000, $"Tombol difficulty {f.Difficulty} tidak terbaca → tidak diklik.");
            if (!InWindow(s, dx, dy))
                return Note("lobby-diff", now, 5000, $"Tombol difficulty {f.Difficulty} di luar jendela Roblox ({dx:0},{dy:0}) → tidak diklik.");
            if (!Ready("lobby-diff", now, 1500)) return [];
            _diffAt = now;
            Status = $"Difficulty terpilih '{s.LobbyDifficultyTag ?? "?"}', target {f.Difficulty}.";
            return [new ClickAction(dx, dy, $"Difficulty {f.Difficulty}")];
        }
        if (_diffAt is { } da && now - da < 700) return [];
        if (DropTarget(s, s.LobbyRewards, s.LobbyRewardsView, f.DropTarget, "lobby-target", now) is { } tgtAct) return tgtAct;
        // v0.2.80: tombol Start wajib terbaca (koordinat cadangan 1920x1080 dihapus).
        if (s.LobbyStart is not { } st) return Note("lobby-start-missing", now, 5000, "Tombol Start tidak terbaca → tidak diklik.");
        var (sx, sy) = (st.CenterX, st.CenterY);
        if (!InWindow(s, sx, sy))
            return Note("lobby-start", now, 5000, $"Tombol Start di luar jendela Roblox ({sx:0},{sy:0}) → tidak diklik.");
        return Ready("lobby-start", now, 3000) ? [new ClickAction(sx, sy, "Start (Create Room)")] : [];
    }

    private static (double X, double Y)? DifficultyPoint(GameSnapshot s, Difficulty d)
    {
        var hit = s.LobbyDifficulties.FirstOrDefault(e => e.Name.Equals(d.ToString(), StringComparison.OrdinalIgnoreCase));
        return hit?.Rect is { } r ? (r.CenterX, r.CenterY) : null;
    }

    /// <summary>Titik ada di dalam area klien jendela Roblox. v0.2.80: ukuran tidak diketahui = BUKAN di dalam.</summary>
    public static bool InWindow(GameSnapshot s, double x, double y) =>
        s.ClientW > 0 && s.ClientH > 0 && (x >= 2 && y >= 2 && x <= s.ClientW - 2 && y <= s.ClientH - 2);

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
        if (!S.Farm.AutoVoteStart) return [];
        // v0.2.79 (jangan menebak): tombol Yes wajib terbaca; koordinat cadangan tidak dipakai.
        if (s.StartVoteYes is not { } r) return Note("vote-yes-missing", now, 5000, "Vote tampil tapi tombol Yes tidak terbaca → tidak diklik.");
        if (!Ready("vote-yes", now, 2000)) return [];
        return [new ClickAction(r.CenterX, r.CenterY, "Vote Yes") { InRound = true }];
    }

    // ---------- Ronde ----------
}

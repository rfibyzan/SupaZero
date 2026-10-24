using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>Hasil kalibrasi aksi: map per mode (terkunci/terbuka) dan daftar act per map, semua dari GUI yang terbaca.</summary>
public sealed record CalibrationScan(IReadOnlyList<KnownMap> Maps, IReadOnlyDictionary<string, List<int>> Acts, IReadOnlyList<string> Notes);

/// <summary>
/// v0.2.82 Kalibrasi aksi (permintaan Byzan 25 Okt 2026): karakter melakukan aksi untuk MENDAPATKAN data, bukan menebak.
/// Lobby: buka menu Trait lalu Spin (data power) → jalan ke room Create Room → tiap mode: klik mode, baca daftar map →
/// tiap map terbuka: klik map, baca ActsList → klik Leave (batal, tidak pernah klik Start/difficulty).
/// Setiap bacaan baru diterima bila sama persis selama <see cref="CalStableMs"/> (daftar GUI berganti bertahap setelah klik).
/// </summary>
public sealed partial class FarmMachine
{
    public bool Calibrating { get; private set; }
    public Action<CalibrationScan>? CalibrationFinished { get; set; }
    public const int CalStableMs = 800, CalStepTimeoutMs = 10_000;

    // 1 baca daftar mode · 2 pilih mode · 3 baca map · 4 pilih map · 5 baca act · 6 Leave
    private int _calStage;
    private List<string> _calModes = [];
    private int _calModeIdx;
    private List<string> _calMaps = [];
    private int _calMapIdx;
    private readonly List<KnownMap> _calKnown = [];
    private readonly Dictionary<string, List<int>> _calActs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _calNotes = [];
    private string? _calSig;
    private long _calSigSince, _calStepAt = -1;

    public void StartCalibration()
    {
        Calibrating = true;
        _calStage = 0; _calModeIdx = _calMapIdx = 0;
        _calModes = []; _calMaps = [];
        _calKnown.Clear(); _calActs.Clear(); _calNotes.Clear();
        _calSig = null; _calStepAt = -1;
        _pcStep = 0; _pcStepAt = null; _pcOpenAt = null;
    }

    /// <summary>Awalan status sekali saja (v0.2.84: Status yang tidak diperbarui tiap tick menumpuk "Kalibrasi: Kalibrasi: …").</summary>
    private void CalPrefix() { if (!Status.StartsWith("Kalibrasi", StringComparison.Ordinal)) Status = "Kalibrasi: " + Status; }

    private bool CalStable(string sig, long now)
    {
        if (sig != _calSig) { _calSig = sig; _calSigSince = now; return false; }
        return now - _calSigSince >= CalStableMs;
    }

    private void CalGo(int stage, long now) { _calStage = stage; _calSig = null; _calStepAt = now; }
    private bool CalTimedOut(long now) => _calStepAt >= 0 && now - _calStepAt > CalStepTimeoutMs;
    private string CalMode => _calModeIdx < _calModes.Count ? _calModes[_calModeIdx] : "";

    private List<MacroAction> CalSkip(string note, int stage, long now)
    {
        _calNotes.Add(note);
        CalGo(stage, now);
        return [new NoteAction("Kalibrasi: " + note)];
    }

    private List<MacroAction> CalibrationStep(GameSnapshot s, AppSettings S, long now)
    {
        if (_calStepAt < 0) _calStepAt = now;
        switch (s.Screen)
        {
            case GameScreen.Unknown:
                return Wait("Kalibrasi: menunggu Roblox / DataModel.");
            case GameScreen.Lobby:
                if (_calStage >= 6) return CalFinish("menu room ditutup lewat Leave");
                if (_pcStep < 2)
                {
                    string key = _pcStep == 0 ? AnimeZeroPaths.MenuTrait : AnimeZeroPaths.MenuSpin;
                    if (!s.HubMenuButtons.Any(b => b.Name == key))
                    {
                        // HUD lobby bisa belum termuat → tunggu sampai 5 dtk sebelum langkah power dilewati.
                        if (now - _calStepAt < 5000) { Status = $"Kalibrasi: menunggu tombol {key} di LobbyHUD."; return [new MoveAction([], "Kalibrasi")]; }
                        _calNotes.Add($"tombol {key} tidak terbaca (LeftSideButtons: {(s.HubButtonNames.Count > 0 ? string.Join(", ", s.HubButtonNames) : "tidak terbaca")}).");
                        _pending.Add(new NoteAction("Kalibrasi: " + _calNotes[^1]));
                    }
                }
                if (PowerCheck(s, now) is { } pc) { CalPrefix(); return pc; }
                if (_calStage > 0) return CalFinish("menu room tertutup sebelum selesai");
                var walk = WalkToLobby(s, S, now, force: true);
                CalPrefix();
                return walk;
            case GameScreen.LobbyMenu:
                return CalMenu(s, now);
            case GameScreen.TrialsMenu when _calStage > 0:
                // Map yang membuka panel lain (bukti log 25 Okt 00:14: "Trials") → simpan hasil, jangan klik apa pun di panel itu.
                _calNotes.Add("panel Trials terbuka dari daftar map → tutup panel Trials sendiri.");
                return CalFinish("panel Trials terbuka");
            default:
                Calibrating = false;
                return Stop($"Kalibrasi hanya bisa dimulai dari lobby (layar sekarang: {s.Screen}).");
        }
    }

    private List<MacroAction> CalMenu(GameSnapshot s, long now)
    {
        Phase = MacroPhase.Lobby;
        var stand = new MoveAction([], "Kalibrasi");
        if (_calStage == 0) CalGo(1, now);

        if (_calStage == 1)
        {
            Status = "Kalibrasi: membaca daftar mode.";
            string sig = string.Join("|", s.LobbyModes.Select(m => $"{m.Name}:{m.Locked}"));
            if (!CalStable(sig, now))
                return CalTimedOut(now) ? CalSkip("daftar mode tidak stabil → hanya mode terpilih.", 1, now) : [stand];
            _calModes = s.LobbyModes.Where(m => !m.Locked).Select(m => m.Name).ToList();
            if (_calModes.Count == 0) _calModes = [""]; // tidak ada daftar mode: pakai yang sedang terpilih saja
            _calModeIdx = 0;
            CalGo(2, now);
        }

        if (_calStage == 2)
        {
            if (_calModeIdx >= _calModes.Count) { CalGo(6, now); }
            else
            {
                string m = CalMode;
                var e = m.Length == 0 ? null : s.LobbyModes.FirstOrDefault(x => x.Name == m);
                if (m.Length == 0 || e is { Selected: true }) CalGo(3, now);
                else if (e is null || CalTimedOut(now)) { _calModeIdx++; return CalSkip($"mode {m} tidak bisa dipilih → dilewati.", 2, now); }
                else
                {
                    Status = $"Kalibrasi: pilih mode {m}.";
                    return [stand, .. ClickEntry(s, e, s.LobbyModesView, "cal-mode", $"Mode {m}", now) ?? []];
                }
            }
        }

        if (_calStage == 3)
        {
            Status = $"Kalibrasi: membaca map mode {(CalMode.Length > 0 ? CalMode : "terpilih")}.";
            string sig = string.Join("|", s.LobbyMaps.Select(x => $"{x.Name}:{x.Locked}:{x.Mode}"));
            if (s.LobbyMaps.Count == 0 || !CalStable(sig, now))
            {
                if (!CalTimedOut(now)) return [stand];
                _calModeIdx++;
                return CalSkip($"daftar map mode {CalMode} tidak terbaca → dilewati.", 2, now);
            }
            foreach (var x in s.LobbyMaps)
            {
                _calKnown.RemoveAll(k => k.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase));
                _calKnown.Add(new KnownMap(x.Name, x.Locked, x.Mode ?? (CalMode.Length > 0 ? CalMode : null)));
            }
            // "Trials" di daftar map membuka panel Trials (bukan act) → tidak diklik saat kalibrasi.
            _calMaps = s.LobbyMaps.Where(x => !x.Locked && !_calActs.ContainsKey(x.Name) && !OpensOtherPanel(x.Name)).Select(x => x.Name).ToList();
            _calMapIdx = 0;
            CalGo(4, now);
        }

        if (_calStage == 4)
        {
            if (_calMapIdx >= _calMaps.Count) { _calModeIdx++; CalGo(2, now); return [stand]; }
            string map = _calMaps[_calMapIdx];
            if (Matches(s.LobbyMapTag, map)) CalGo(5, now);
            else
            {
                var e = s.LobbyMaps.FirstOrDefault(x => x.Name == map);
                if (e is null || CalTimedOut(now)) { _calMapIdx++; return CalSkip($"map {map} tidak bisa dipilih → act-nya tidak terbaca.", 4, now); }
                Status = $"Kalibrasi: pilih map {map}.";
                return [stand, .. ClickEntry(s, e, s.LobbyMapsView, "cal-map", $"Map {map}", now) ?? []];
            }
        }

        if (_calStage == 5)
        {
            string map = _calMaps[_calMapIdx];
            Status = $"Kalibrasi: membaca act map {map}.";
            var nums = s.LobbyActs.Select(a => ActNumber(a.Name)).Where(n => n > 0).Distinct().Order().ToList();
            string sig = $"{s.LobbyMapTag}|{string.Join(",", s.LobbyActs.Select(a => $"{a.Name}:{a.Locked}"))}";
            if (nums.Count == 0 || !Matches(s.LobbyMapTag, map) || !CalStable(sig, now))
            {
                if (!CalTimedOut(now)) return [stand];
                _calMapIdx++;
                return CalSkip($"act map {map} tidak terbaca.", 4, now);
            }
            _calActs[map] = nums;
            int locked = s.LobbyActs.Count(a => a.Locked);
            var note = new NoteAction($"Kalibrasi: {map} → Act {string.Join(", ", nums)}{(locked > 0 ? $" ({locked} terkunci)" : "")}.");
            _calMapIdx++;
            CalGo(4, now);
            return [stand, note];
        }

        // 6) Batalkan room: klik Leave, tidak pernah Start.
        Status = "Kalibrasi selesai membaca → Leave (batalkan room).";
        if (s.LobbyLeave is not { } lv)
            return CalFinish("tombol Leave tidak terbaca → tutup menu room sendiri");
        if (CalTimedOut(now)) return CalFinish("menu room tidak tertutup setelah klik Leave");
        if (!InWindow(s, lv.CenterX, lv.CenterY)) return CalFinish("tombol Leave di luar jendela Roblox → tutup menu room sendiri");
        return Ready("cal-leave", now, 3000) ? [stand, new ClickAction(lv.CenterX, lv.CenterY, "Leave (batalkan room kalibrasi)")] : [stand];
    }

    /// <summary>Entri daftar map yang terbukti membuka panel lain (log Byzan 25 Okt 2026 00:14: klik "Trials" → TrialsMenu).</summary>
    public static bool OpensOtherPanel(string map) => map.Trim().Equals("Trials", StringComparison.OrdinalIgnoreCase);

    private List<MacroAction> CalFinish(string how)
    {
        Calibrating = false;
        var scan = new CalibrationScan(_calKnown.ToList(), new Dictionary<string, List<int>>(_calActs, StringComparer.OrdinalIgnoreCase), _calNotes.ToList());
        CalibrationFinished?.Invoke(scan);
        string acts = scan.Acts.Count == 0 ? "act tidak terbaca" : string.Join("; ", scan.Acts.Select(kv => $"{kv.Key} Act {string.Join(",", kv.Value)}"));
        return Stop($"Kalibrasi selesai ({how}): {scan.Maps.Count} map ({scan.Maps.Count(m => !m.Locked)} terbuka) · {acts}.");
    }
}

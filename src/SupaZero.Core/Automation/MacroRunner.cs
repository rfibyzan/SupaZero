using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>
/// Loop macro di thread latar: baca snapshot → <see cref="StoryMachine"/> → kirim input. Input HANYA dikirim saat jendela
/// Roblox di depan (alt-tab = pause otomatis). Tidak ada tulis memori.
/// </summary>
public sealed class MacroRunner(Func<GameSnapshot> read, IInput input, Func<AppSettings> settings) : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public FarmMachine Machine { get; } = new(settings);
    private readonly HashSet<ushort> _held = [];
    public bool IsRunning => _loop is { IsCompleted: false };
    public GameSnapshot? LastSnapshot { get; private set; }

    public event Action<string>? Log;
    public event Action<bool>? RunningChanged;
    public event Action? Ticked;

    public void Toggle()
    {
        if (IsRunning) Stop(); else Start();
    }

    public void Start(bool calibrate = false)
    {
        if (IsRunning) return;
        Machine.Reset();
        if (calibrate) Machine.StartCalibration();
        Machine.Diagnostic ??= m => Log?.Invoke(m);
        LastStopReason = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => Loop(token), token);
        Log?.Invoke(calibrate ? "Kalibrasi aksi mulai (power → room → mode/map/act → Leave)." : "Macro mulai.");
        RunningChanged?.Invoke(true);
    }

    /// <summary>Alasan StopAction terakhir (null = dihentikan manual).</summary>
    public string? LastStopReason { get; set; }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts = null;
        ReleaseMovement();
        Log?.Invoke("Macro berhenti.");
        RunningChanged?.Invoke(false);
    }

    private async Task Loop(CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool pausedLogged = false;
        while (!ct.IsCancellationRequested)
        {
            int tick = Math.Clamp(settings().General.TickMs, 30, 2000);
            try
            {
                var snap = read();
                if (input.ClientSize() is { } cs) snap = snap with { ClientW = cs.W, ClientH = cs.H };
                // v0.2.80 (jangan menebak): status shift lock HANYA dari geser kamera di memori; deteksi kursor-di-tengah dihapus.
                if (snap.CameraFollowsPlayer is { } follow && follow != _camFollow)
                {
                    _camFollow = follow;
                    if (!follow) Log?.Invoke($"Kamera tidak mengikuti karakter (animasi/cutscene; subject {snap.Camera?.Subject ?? "?"}, FOV {snap.Camera?.Fov:0.#}) → shift lock, putar & arah kamera ditahan.");
                    else Log?.Invoke("Kamera kembali mengikuti karakter.");
                }
                _camFree = snap.CameraFollowsPlayer == false;
                var sl = snap.ShiftLockFromCamera;
                if (sl != _shiftLockMem && sl is not null) Log?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Shift lock {(sl == true ? "NYALA" : "mati")} (kamera geser {snap.CameraSideOffset:0.00} stud)."));
                _shiftLockMem = sl;
                snap = snap with { ShiftLock = sl == true };
                LastSnapshot = snap;
                var actions = Machine.Step(snap, clock.ElapsedMilliseconds);
                bool fg = input.IsTargetForeground();
                // v0.3.12 (Byzan 26 Okt): musuh terdeteksi di stage mana pun → shift lock WAJIB nyala (tidak tergantung toggle).
                bool mustLock = MustShiftLock(snap);
                if (fg && (mustLock || settings().Combat.AutoShiftLock)) await EnsureShiftLock(snap.Screen, actions, clock.ElapsedMilliseconds, ct, mustLock);
                if (!fg && actions.Any(a => a is not NoteAction and not StopAction))
                {
                    if (!pausedLogged) Log?.Invoke("Roblox tidak di depan → input ditahan.");
                    pausedLogged = true;
                }
                else pausedLogged = false;
                foreach (var a in actions)
                {
                    if (ct.IsCancellationRequested) break;
                    await Execute(a, fg, ct);
                }
                Ticked?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log?.Invoke("Error: " + ex.Message); }
            try { await Task.Delay(tick, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private const ushort ShiftVk = 0xA0; // VK_LSHIFT = tombol Shift Lock bawaan Roblox
    /// <summary>Status shift lock dari kamera: true/false pasti, null = tidak terbaca (animasi, geser kamera ragu).</summary>
    private bool? _shiftLockMem;
    private bool? _camFollow;
    private bool _camFree;
    private CancellationTokenSource? _turnCts;

    // v0.2.72: shift lock otomatis. Ronde (InRound) → nyala; layar lain (vote, hasil, lobby, menu) → mati agar klik GUI kena.
    // v0.2.80 (jangan menebak): Shift hanya ditekan bila status terbaca pasti dari kamera, dan hasilnya tidak diasumsikan:
    // tick berikutnya membaca ulang. Bila 2,5 dtk setelah menekan status belum sesuai → dihitung gagal; 3× gagal → berhenti di layar ini.
    private long _slLastPress = -10_000;
    private int _slFails;
    private bool? _slWant;
    private GameScreen _slScreen = GameScreen.Unknown;

    /// <summary>v0.3.12: musuh hidup terbaca dan bukan layar lobby/menu lobby (Shift dilarang di lobby).</summary>
    public static bool MustShiftLock(GameSnapshot s) => MustShiftLock(s.Screen, s.EnemyCount);
    public static bool MustShiftLock(GameScreen screen, int enemyCount) =>
        enemyCount > 0 && screen is not (GameScreen.Lobby or GameScreen.LobbyMenu or GameScreen.QueueWaiting or GameScreen.TrialsMenu);

    private long _slGiveUpLogAt = -60_000;

    private async Task EnsureShiftLock(GameScreen screen, IReadOnlyList<MacroAction> actions, long now, CancellationToken ct, bool mustLock = false)
    {
        if (screen != _slScreen) { _slScreen = screen; _slFails = 0; _slWant = null; }
        // Status shift lock hanya dari kamera (tanpa tebakan): tidak terbaca / kamera animasi → tidak menekan Shift.
        if ((screen == GameScreen.Unknown && !mustLock) || _camFree || _shiftLockMem is not { } cur) return;
        // Klik GUI di tick ini: penanganan klik mematikan & menyalakan lagi shift lock sendiri.
        bool clicking = actions.Any(a => a is ClickAction or ScrollAction);
        bool want = (mustLock || screen == GameScreen.InRound) && !clicking;
        if (cur == want) { if (_slWant == want) _slFails = 0; _slWant = null; return; }
        if (now - _slLastPress < 2500) return;
        if (_slFails >= 3)
        {
            // Musuh ada → tidak pernah menyerah; coba lagi tiap 6 dtk. Tanpa musuh → berhenti di layar ini (perilaku lama).
            if (!(mustLock && want) || now - _slLastPress < 6000) return;
            if (now - _slGiveUpLogAt > 60_000) { _slGiveUpLogAt = now; Log?.Invoke("Shift lock belum nyala padahal ada musuh → terus dicoba tiap 6 dtk (cek Settings Roblox: Shift Lock Switch = On)."); }
        }
        else if (_slWant == want && ++_slFails >= 3)
        {
            Log?.Invoke($"Shift lock tidak berubah ke {(want ? "nyala" : "mati")} setelah 3× Shift (cek Settings Roblox: Shift Lock Switch){(mustLock && want ? " → musuh ada, tetap dicoba tiap 6 dtk." : " → berhenti mencoba di layar ini.")}");
            if (!(mustLock && want)) return;
        }
        _slWant = want;
        _slLastPress = now;
        input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
        Log?.Invoke(want ? (mustLock ? "Musuh terdeteksi → Shift ditekan (shift lock wajib nyala, diverifikasi dari kamera)." : "Ronde berjalan → Shift ditekan (menyalakan shift lock, diverifikasi dari kamera).") : $"Di luar ronde ({screen}) → Shift ditekan (mematikan shift lock untuk klik GUI).");
        await Task.Delay(120, ct);
    }

    /// <summary>Putar kamera halus: delta dipecah jadi langkah kecil tiap ~8 ms sepanjang satu tick, di latar belakang.</summary>
    private long _recenterLogAt = -10_000;

    /// <summary>Kursor di dalam area klien Roblox (margin 2 px) dan Roblox di depan.</summary>
    private bool CursorInside()
    {
        if (!input.IsTargetForeground() || input.ClientSize() is not { } sz || input.CursorClient() is not { } c) return false;
        return c.X >= 2 && c.Y >= 2 && c.X < sz.W - 2 && c.Y < sz.H - 2;
    }

    private void RecenterCursor(string why)
    {
        _turnCts?.Cancel();
        if (input.IsTargetForeground() && input.ClientSize() is { } sz) input.MoveClient(sz.W / 2.0, sz.H / 2.0);
        long now = Environment.TickCount64;
        if (now - _recenterLogAt > 5000) { _recenterLogAt = now; Log?.Invoke($"Kursor keluar jendela Roblox saat {why} → ditarik ke tengah, klik dibatalkan."); }
    }

    private void SmoothTurn(int dx, int dy, int durationMs)
    {
        _turnCts?.Cancel();
        var cts = _turnCts = new CancellationTokenSource();
        int steps = Math.Clamp(durationMs / 8, 1, 40);
        _ = Task.Run(async () =>
        {
            int sx = 0, sy = 0;
            for (int i = 1; i <= steps && !cts.IsCancellationRequested; i++)
            {
                int tx = (int)Math.Round((double)dx * i / steps), ty = (int)Math.Round((double)dy * i / steps);
                input.MoveRelative(tx - sx, ty - sy);
                sx = tx; sy = ty;
                // v0.2.76: shift lock lepas (mis. setelah Ultimate) → gerak relatif menyeret kursor keluar jendela. Berhenti & tarik balik.
                if (!CursorInside()) { RecenterCursor("putar kamera"); break; }
                try { await Task.Delay(8, cts.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }

    /// <summary>Kursor ada di titik klien (x,y) ±3 px.</summary>
    private bool CursorAt(double x, double y) => input.CursorClient() is { } c && Math.Abs(c.X - x) <= 3 && Math.Abs(c.Y - y) <= 3;

    private async Task ClickVerified(ClickAction c, CancellationToken ct)
    {
        _turnCts?.Cancel();
        bool toggled = false;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            input.MoveClient(c.X, c.Y);
            await Task.Delay(90, ct);
            if (!input.IsTargetForeground()) { Log?.Invoke($"Klik {c.Reason} dibatalkan: Roblox tidak di depan."); break; }
            if (CursorAt(c.X, c.Y))
            {
                Log?.Invoke($"Klik {c.Reason} ({c.X:0},{c.Y:0})");
                input.ClickClient(c.X, c.Y);
                break;
            }
            if (attempt == 1)
            {
                Log?.Invoke($"Klik {c.Reason} dibatalkan: kursor tidak bisa ditaruh di ({c.X:0},{c.Y:0}) (tetap ditarik Roblox).");
                break;
            }
            Log?.Invoke($"Kursor ditarik ke tengah (shift lock aktif) → Shift lalu cek ulang sebelum klik {c.Reason}.");
            input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
            toggled = true;
            await Task.Delay(150, ct);
        }
        if (toggled && c.InRound)
        {
            // Kembalikan gaya main (shift lock nyala) di dalam stage; hasilnya dibaca ulang dari kamera tick berikutnya.
            await Task.Delay(150, ct);
            input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
        }
    }

    private async Task Execute(MacroAction a, bool foreground, CancellationToken ct)
    {
        switch (a)
        {
            case NoteAction n:
                Log?.Invoke(n.Reason);
                return;
            case StopAction s:
                Log?.Invoke(s.Reason);
                LastStopReason = s.Reason;
                Stop();
                return;
        }
        if (!foreground)
        {
            ReleaseMovement();
            return;
        }
        switch (a)
        {
            case MoveAction m:
                foreach (ushort k in _held.Where(k => !m.Keys.Contains(k)).ToList()) { input.KeyUp(k); _held.Remove(k); }
                foreach (ushort k in m.Keys.Where(k => _held.Add(k))) input.KeyDown(k);
                break;
            case ClickAction c:
                if (input.ClientSize() is { } size && (c.X < 0 || c.Y < 0 || c.X >= size.W || c.Y >= size.H))
                {
                    Log?.Invoke($"Klik {c.Reason} dibatalkan: ({c.X:0},{c.Y:0}) di luar jendela Roblox {size.W}x{size.H}.");
                    break;
                }
                // v0.2.80 (jangan menebak): tidak bergantung pada status shift lock. Kursor dipindah ke target lalu posisinya dibaca
                // ulang dari Windows: tetap di target = klik akan kena; ditarik Roblox ke tengah = shift lock aktif → Shift, ulangi verifikasi.
                // Gagal 2× → klik dibatalkan.
                await ClickVerified(c, ct);
                break;
            case ScrollAction sc:
                Log?.Invoke($"Scroll {sc.Reason} ({sc.X:0},{sc.Y:0}) x{Math.Abs(sc.Notches)}");
                input.ScrollClient(sc.X, sc.Y, sc.Notches);
                break;
            case AimAction or TurnAction when _camFree:
                break; // v0.2.77: kamera sedang dipindah animasi → arah layar tidak valid
            // v0.2.80: bidik kursor & drag kamera hanya bila shift lock PASTI mati; putar relatif hanya bila PASTI nyala.
            case AimAction or TurnAction { Drag: true } when _shiftLockMem != false:
                break;
            case TurnAction { Drag: false } when _shiftLockMem != true:
                break;
            case AimAction am:
                if (input.ClientSize() is { } sz && am.X >= 0 && am.Y >= 0 && am.X < sz.W && am.Y < sz.H)
                    input.MoveClient(am.X, am.Y);
                break;
            case TurnAction { Drag: true } t:
                // Tanpa shift lock: tahan klik kanan, geser relatif bertahap, lalu lepas (kamera Roblox ikut berputar).
                _turnCts?.Cancel();
                input.RightDown();
                await Task.Delay(30, ct);
                {
                    int steps = 12, sx = 0, sy = 0;
                    for (int i = 1; i <= steps; i++)
                    {
                        int tx = (int)Math.Round((double)t.Dx * i / steps), ty = (int)Math.Round((double)t.Dy * i / steps);
                        input.MoveRelative(tx - sx, ty - sy);
                        sx = tx; sy = ty;
                        await Task.Delay(8, ct);
                    }
                }
                await Task.Delay(20, ct);
                input.RightUp();
                if (!CursorInside()) RecenterCursor("drag kamera");
                break;
            case TurnAction t:
                SmoothTurn(t.Dx, t.Dy, (int)(Math.Clamp(settings().General.TickMs, 30, 2000) * 0.9));
                break;
            case AttackAction:
                // v0.2.76: klik di posisi kursor sekarang hanya bila kursor di dalam jendela Roblox. Setelah Ultimate kursor pernah
                // terseret ke luar dan serangan dasar mengklik browser/File Explorer (laporan Byzan 24 Okt 23:24).
                if (!CursorInside()) { RecenterCursor("serangan"); break; }
                input.ClickHere();
                break;
            case KeyAction k:
                input.KeyDown(k.VirtualKey);
                await Task.Delay(Math.Max(10, k.HoldMs), ct);
                input.KeyUp(k.VirtualKey);
                break;
        }
    }

    private void ReleaseMovement()
    {
        lock (_held)
        {
            foreach (ushort k in _held) input.KeyUp(k);
            _held.Clear();
        }
    }

    public void Dispose() => Stop();
}

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

    public void Start()
    {
        if (IsRunning) return;
        Machine.Reset();
        Machine.Diagnostic ??= m => Log?.Invoke(m);
        LastStopReason = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => Loop(token), token);
        Log?.Invoke("Macro mulai.");
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
                DetectShiftLock(input.IsTargetForeground());
                // v0.2.75: bila geser kamera terbaca dari memori, itu sumber utama status shift lock (deteksi kursor jadi cadangan).
                if (snap.ShiftLockFromCamera is { } camLock)
                {
                    if (camLock != _shiftLockMem) Log?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"Shift lock {(camLock ? "NYALA" : "mati")} (kamera geser {snap.CameraSideOffset:0.00} stud)."));
                    _shiftLockMem = camLock;
                    _shiftLock = camLock; _centeredTicks = camLock ? 5 : 0; _offTicks = 0;
                }
                snap = snap with { ShiftLock = _shiftLock };
                LastSnapshot = snap;
                var actions = Machine.Step(snap, clock.ElapsedMilliseconds);
                bool fg = input.IsTargetForeground();
                if (fg && settings().Combat.AutoShiftLock) await EnsureShiftLock(snap.Screen, actions, clock.ElapsedMilliseconds, ct);
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

    // ── Deteksi shift lock: Roblox mengembalikan kursor ke tengah jendela tiap frame saat shift lock aktif.
    private const ushort ShiftVk = 0xA0; // VK_LSHIFT = tombol Shift Lock bawaan Roblox
    private bool _shiftLock;
    private bool? _shiftLockMem;
    private int _centeredTicks, _offTicks;
    private (double X, double Y)? _lastAimFar;
    private CancellationTokenSource? _turnCts;

    // v0.2.72: shift lock otomatis. Ronde (InRound) → nyala; layar lain (vote, hasil, lobby, menu) → mati agar klik GUI kena.
    // Shift ditekan paling sering tiap 2,5 dtk; setelah menyalakan, dianggap nyala (deteksi kursor-di-tengah butuh beberapa tick).
    // 3× gagal berturut-turut (mis. Shift Lock dimatikan di setelan Roblox) → berhenti mencoba sampai layar berganti.
    private long _slLastPress = -10_000;
    private int _slFails;
    private GameScreen _slScreen = GameScreen.Unknown;

    private async Task EnsureShiftLock(GameScreen screen, IReadOnlyList<MacroAction> actions, long now, CancellationToken ct)
    {
        if (screen != _slScreen) { _slScreen = screen; _slFails = 0; }
        if (screen == GameScreen.Unknown || now - _slLastPress < 2500) return;
        bool want = screen == GameScreen.InRound && !actions.Any(a => a is ClickAction or ScrollAction);
        // Kursor diam di tengah 1-4 tick belum pasti shift lock → tunggu deteksi dulu, jangan tekan Shift (bisa membalik).
        if (!_shiftLock && _centeredTicks > 0) return;
        if (want == _shiftLock) { if (want && _centeredTicks >= 5) _slFails = 0; return; }
        if (want && _slFails >= 3) return;
        _slLastPress = now;
        input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
        if (want)
        {
            _slFails++;
            _shiftLock = true; _offTicks = 0; _centeredTicks = 0;
            Log?.Invoke(_slFails >= 3 ? "Shift lock tidak mau nyala 3× (cek Settings Roblox: Shift Lock Switch aktif?) → berhenti mencoba di layar ini." : "Ronde berjalan → shift lock dinyalakan.");
        }
        else
        {
            _shiftLock = false; _centeredTicks = 0; _offTicks = 0;
            Log?.Invoke($"Di luar ronde ({screen}) → shift lock dimatikan untuk klik GUI.");
        }
        await Task.Delay(120, ct);
    }

    private void DetectShiftLock(bool fg)
    {
        if (!fg || input.ClientSize() is not { } sz || input.CursorClient() is not { } c) { _lastAimFar = null; return; }
        double off = Math.Max(Math.Abs(c.X - sz.W / 2.0), Math.Abs(c.Y - sz.H / 2.0));
        bool centered = off <= 4;
        bool was = _shiftLock;
        if (!centered)
        {
            _centeredTicks = 0;
            // Saat terkunci, putaran kamera halus bisa sesaat menggeser kursor sebelum Roblox mengembalikannya ke tengah:
            // baru dianggap mati bila kursor jauh (> 30 px) dari tengah 3 tick berturut-turut.
            if (!_shiftLock || (off > 30 && ++_offTicks >= 3)) { _shiftLock = false; _offTicks = 0; }
        }
        else
        {
            _offTicks = 0;
            _centeredTicks++;
            // Kursor digeser jauh dari tengah tick lalu, tapi kini kembali di tengah → pasti terkunci.
            if (_lastAimFar is not null || _centeredTicks >= 5) _shiftLock = true;
        }
        _lastAimFar = null;
        if (was != _shiftLock) Log?.Invoke(_shiftLock ? "Shift lock terdeteksi → bidik dengan memutar kamera." : "Shift lock mati → bidik dengan kursor.");
    }

    /// <summary>Putar kamera halus: delta dipecah jadi langkah kecil tiap ~8 ms sepanjang satu tick, di latar belakang.</summary>
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
                try { await Task.Delay(8, cts.Token); } catch (OperationCanceledException) { break; }
            }
        });
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
                // Shift lock aktif: kursor dikunci di tengah, klik GUI (Vote Yes, Retry, Next) jatuh di tengah layar.
                // → matikan shift lock (Shift), klik, lalu nyalakan lagi agar gaya main tetap sama.
                // v0.2.58: hanya bila shift lock sudah pasti terdeteksi. Kursor yang kebetulan diam di tengah (lobby) bukan shift lock:
                // menekan Shift di situ justru MENYALAKAN shift lock dan klik kartu Teleport jatuh di tengah layar.
                // Di dalam stage (Vote/Retry/Next) kursor yang diam di tengah ≥ 2 tick juga dianggap shift lock (perilaku v0.2.54 yang
                // membuat Retry jalan); di lobby tetap wajib terdeteksi pasti.
                bool unlock = _shiftLock || (c.InRound && _centeredTicks >= 2);
                if (unlock)
                {
                    Log?.Invoke($"Shift lock aktif → dimatikan sementara untuk klik {c.Reason}.");
                    input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
                    await Task.Delay(150, ct);
                }
                Log?.Invoke($"Klik {c.Reason} ({c.X:0},{c.Y:0})");
                input.ClickClient(c.X, c.Y);
                if (unlock)
                {
                    await Task.Delay(150, ct);
                    input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
                    _centeredTicks = 0;
                }
                break;
            case ScrollAction sc:
                Log?.Invoke($"Scroll {sc.Reason} ({sc.X:0},{sc.Y:0}) x{Math.Abs(sc.Notches)}");
                input.ScrollClient(sc.X, sc.Y, sc.Notches);
                break;
            case AimAction am:
                // Kursor sedang di tengah tapi shift lock belum pasti → jangan geser absolut (bisa menyentak kamera).
                if (_centeredTicks > 0 && !_shiftLock) break;
                if (input.ClientSize() is { } sz && am.X >= 0 && am.Y >= 0 && am.X < sz.W && am.Y < sz.H)
                {
                    input.MoveClient(am.X, am.Y);
                    if (Math.Abs(am.X - sz.W / 2.0) > 40 || Math.Abs(am.Y - sz.H / 2.0) > 40) _lastAimFar = (am.X, am.Y);
                }
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
                break;
            case TurnAction t:
                SmoothTurn(t.Dx, t.Dy, (int)(Math.Clamp(settings().General.TickMs, 30, 2000) * 0.9));
                break;
            case AttackAction:
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

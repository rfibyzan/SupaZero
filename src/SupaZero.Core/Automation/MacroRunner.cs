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
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => Loop(token), token);
        Log?.Invoke("Macro mulai.");
        RunningChanged?.Invoke(true);
    }

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
                snap = snap with { ShiftLock = _shiftLock };
                LastSnapshot = snap;
                var actions = Machine.Step(snap, clock.ElapsedMilliseconds);
                bool fg = input.IsTargetForeground();
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
    private bool _shiftLock;
    private int _centeredTicks, _offTicks;
    private (double X, double Y)? _lastAimFar;
    private CancellationTokenSource? _turnCts;

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
                Log?.Invoke($"Klik {c.Reason} ({c.X:0},{c.Y:0})");
                input.ClickClient(c.X, c.Y);
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

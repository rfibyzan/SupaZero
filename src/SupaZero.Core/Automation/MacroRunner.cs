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
                if (input.ClientSize() is { } sz && am.X >= 0 && am.Y >= 0 && am.X < sz.W && am.Y < sz.H)
                    input.MoveClient(am.X, am.Y);
                break;
            case TurnAction t:
                input.MoveRelative(t.Dx, t.Dy);
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

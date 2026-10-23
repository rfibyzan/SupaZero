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

    public StoryMachine Machine { get; } = new(settings);
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
        if (!foreground) return;
        switch (a)
        {
            case ClickAction c:
                Log?.Invoke($"Klik {c.Reason} ({c.X:0},{c.Y:0})");
                input.ClickClient(c.X, c.Y);
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

    public void Dispose() => Stop();
}

using RIE.Core.Offsets;
using SupaZero.Core.Game;
using SupaZero.Core.Interop;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>
/// v0.3.25 (Byzan 27 Oct): carry on several Roblox windows (SupaBlox multi-instance). One read-only session + one macro per checked PID;
/// only the window in front gets input (SendInput), so this class brings the window that needs input to the front, one at a time.
/// A window whose own damage is at/above "Carry: stop at damage" (idle until the round ends) is skipped. Unknown state = not served.
/// Focus is only switched while one of the carry windows is already in front (alt-tab to another app = pause, as before).
/// </summary>
public sealed class CarryRotation(Func<AppSettings> settings) : IDisposable
{
    public const int SliceMs = 4000, SwitchPauseMs = 3000;

    public sealed class Alt(RobloxProcessInfo info, RobloxSession session, Win32Input input, MacroRunner runner)
    {
        public RobloxProcessInfo Info { get; } = info;
        public RobloxSession Session { get; } = session;
        public Win32Input Input { get; } = input;
        public MacroRunner Runner { get; } = runner;
        public long LastServed { get; set; }
        public string Name => Info.Player ?? $"PID {Info.Pid}";
    }

    private readonly List<Alt> _alts = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public IReadOnlyList<Alt> Alts => _alts;
    public bool IsRunning => _loop is { IsCompleted: false };
    public string? FrontName { get; private set; }
    public event Action<string>? Log;
    /// <summary>v0.3.26: lets the app hook each window's machine (codes list / results).</summary>
    public Action<FarmMachine>? ConfigureMachine { get; set; }
    public event Action<bool>? RunningChanged;

    public void Start(IReadOnlyList<RobloxProcessInfo> picks, OffsetsSnapshot offsets)
    {
        if (IsRunning || picks.Count == 0) return;
        DisposeAlts();
        foreach (var p in picks)
        {
            var session = new RobloxSession();
            session.UseOffsets(offsets);
            session.Pin(p.Pid);
            var input = new Win32Input(() => session.ProcessId);
            var gate = new object();
            var runner = new MacroRunner(() => { lock (gate) return session.ReadSnapshot(); }, input, settings) { StepOnlyInForeground = true };
            var alt = new Alt(p, session, input, runner);
            runner.LogPrefix = $"[{alt.Name}] ";
            ConfigureMachine?.Invoke(runner.Machine);
            runner.Log += m => Log?.Invoke(m);
            _alts.Add(alt);
        }
        foreach (var a in _alts) a.Runner.Start();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => Schedule(ct), ct);
        Log?.Invoke($"Carry rotation started: {string.Join(", ", _alts.Select(a => $"{a.Name} (PID {a.Info.Pid})"))}. Click one of these windows to begin.");
        RunningChanged?.Invoke(true);
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts = null;
        foreach (var a in _alts) a.Runner.Stop();
        FrontName = null;
        Log?.Invoke("Carry rotation stopped.");
        RunningChanged?.Invoke(false);
    }

    public void Toggle(IReadOnlyList<RobloxProcessInfo> picks, OffsetsSnapshot offsets)
    {
        if (IsRunning) Stop(); else Start(picks, offsets);
    }

    /// <summary>Does this window need input? Only from what is read (no guessing): unknown screen = no.</summary>
    public static bool NeedsInput(GameSnapshot? s, int carryDamage)
    {
        if (s is null || !s.Attached || s.Screen == GameScreen.Unknown) return false;
        bool reached = carryDamage > 0 && s.MyDamage is { } d && d >= carryDamage;
        if (s.Screen == GameScreen.InRound) return !reached;
        return true;
    }

    /// <summary>Index of the window to have in front: the current one while it needs input and its slice runs (or nobody else needs input);
    /// otherwise the window needing input that was served longest ago. -1 = keep whatever is in front.</summary>
    public static int Pick(IReadOnlyList<(bool Needs, long LastServed)> alts, int current, long now, long currentSince, int sliceMs = SliceMs)
    {
        bool curNeeds = current >= 0 && current < alts.Count && alts[current].Needs;
        int best = -1;
        for (int i = 0; i < alts.Count; i++)
            if (i != current && alts[i].Needs && (best < 0 || alts[i].LastServed < alts[best].LastServed)) best = i;
        if (curNeeds && (best < 0 || now - currentSince < sliceMs)) return current;
        return best;
    }

    private async Task Schedule(CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long since = 0;
        int lastFront = -2;
        bool pausedLogged = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_alts.All(a => !a.Runner.IsRunning)) { Log?.Invoke("All carry windows stopped."); Stop(); break; }
                long now = clock.ElapsedMilliseconds;
                int front = _alts.FindIndex(a => a.Runner.IsRunning && a.Input.IsTargetForeground());
                if (front != lastFront)
                {
                    since = now;
                    if (front >= 0) _alts[front].LastServed = now;
                    lastFront = front;
                    FrontName = front >= 0 ? _alts[front].Name : null;
                }
                if (front < 0)
                {
                    if (!pausedLogged) Log?.Invoke("Carry rotation: no carry window in front → paused (click one of the carry windows).");
                    pausedLogged = true;
                }
                else
                {
                    pausedLogged = false;
                    if (front >= 0) _alts[front].LastServed = now;
                    int target = settings().Combat.CarryTarget;
                    var states = _alts.Select(a => (a.Runner.IsRunning && NeedsInput(a.Runner.LastSnapshot, target), a.LastServed)).ToList();
                    // The window in front is "served" right now; compare the others by when they were last in front.
                    int pick = Pick(states, front, now, since);
                    if (pick >= 0 && pick != front)
                    {
                        var cur = _alts[front];
                        var next = _alts[pick];
                        bool paused = await cur.Runner.PauseForSwitchAsync(SwitchPauseMs);
                        bool ok = next.Input.BringToFront();
                        cur.Runner.Hold = false;
                        Log?.Invoke(ok
                            ? $"Carry rotation: {cur.Name} → {next.Name}{(paused ? "" : " (previous input still busy)")}."
                            : $"Carry rotation: could not bring {next.Name} to the front.");
                        if (!ok) next.LastServed = now; // try the others first next time
                    }
                }
            }
            catch (Exception ex) { Log?.Invoke("Carry rotation error: " + ex.Message); }
            try { await Task.Delay(250, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private void DisposeAlts()
    {
        foreach (var a in _alts) { a.Runner.Dispose(); a.Session.Dispose(); }
        _alts.Clear();
    }

    public void Dispose() { Stop(); DisposeAlts(); }
}

using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>
/// Macro loop on a background thread: read snapshot → <see cref="StoryMachine"/> → send input. Input is ONLY sent while the
/// Roblox window is in the foreground (alt-tab = automatic pause). No memory writes.
/// </summary>
public sealed class MacroRunner(Func<GameSnapshot> read, IInput input, Func<AppSettings> settings) : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public FarmMachine Machine { get; } = new(settings);
    private readonly HashSet<ushort> _held = [];
    public bool IsRunning => _loop is { IsCompleted: false };
    public GameSnapshot? LastSnapshot { get; private set; }

    // v0.3.25 carry rotation: the rotation pauses input while it switches windows; WantsInput = last tick decided real input.
    private readonly SemaphoreSlim _gate = new(1, 1);
    public volatile bool Hold;
    public bool WantsInput { get; private set; }
    /// <summary>v0.3.25 carry rotation: the machine only steps while this window is in front (background windows are only read).</summary>
    public bool StepOnlyInForeground { get; init; }
    public bool IsForeground => !Hold && input.IsTargetForeground();
    public string LogPrefix { get; set; } = string.Empty;

    /// <summary>Wait until the current tick's input is done, then hold input and release held keys (before another window comes to the front).</summary>
    public async Task<bool> PauseForSwitchAsync(int timeoutMs)
    {
        Hold = true;
        if (!await _gate.WaitAsync(timeoutMs)) return false;
        try { ReleaseMovement(); return true; }
        finally { _gate.Release(); }
    }

    public static bool IsRealInput(MacroAction a) => a is not (NoteAction or StopAction) && !(a is MoveAction m && m.Keys.Count == 0);

    private Action<string>? _log;
    public event Action<string>? Log { add => _log += value; remove => _log -= value; }
    private void Emit(string m) => _log?.Invoke(LogPrefix + m);
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
        Machine.Diagnostic ??= Emit;
        LastStopReason = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => Loop(token), token);
        Emit(calibrate ? "Action calibration started (power → room → mode/map/act → Leave)." : "Macro started.");
        RunningChanged?.Invoke(true);
    }

    /// <summary>Reason of the last StopAction (null = stopped manually).</summary>
    public string? LastStopReason { get; set; }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts = null;
        ReleaseMovement();
        Emit("Macro stopped.");
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
                // v0.2.80 (no guessing): shift lock state ONLY from the camera offset in memory; cursor-in-center detection removed.
                if (snap.CameraFollowsPlayer is { } follow && follow != _camFollow)
                {
                    _camFollow = follow;
                    if (!follow) Emit($"Camera is not following the character (animation/cutscene; subject {snap.Camera?.Subject ?? "?"}, FOV {snap.Camera?.Fov:0.#}) → shift lock, turning & camera aim held.");
                    else Emit("Camera is following the character again.");
                }
                _camFree = snap.CameraFollowsPlayer == false;
                var sl = snap.ShiftLockFromCamera;
                if (sl != _shiftLockMem && sl is not null) Emit(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Shift lock {(sl == true ? "ON" : "off")} (camera offset {snap.CameraSideOffset:0.00} stud)."));
                _shiftLockMem = sl;
                snap = snap with { ShiftLock = sl == true };
                LastSnapshot = snap;
                if (StepOnlyInForeground && !IsForeground)
                {
                    WantsInput = false;
                    Ticked?.Invoke();
                    try { await Task.Delay(tick, ct); } catch (OperationCanceledException) { break; }
                    continue;
                }
                var actions = Machine.Step(snap, clock.ElapsedMilliseconds);
                WantsInput = actions.Any(IsRealInput);
                await _gate.WaitAsync(ct);
                try
                {
                bool fg = !Hold && input.IsTargetForeground();
                // v0.3.12 (Byzan 26 Oct): enemies detected in any stage → shift lock MUST be on (independent of the toggle).
                bool mustLock = MustShiftLock(snap);
                if (fg && (mustLock || settings().Combat.AutoShiftLock)) await EnsureShiftLock(snap.Screen, actions, clock.ElapsedMilliseconds, ct, mustLock);
                if (!fg && actions.Any(a => a is not NoteAction and not StopAction))
                {
                    if (!pausedLogged) Emit("Roblox is not in the foreground → input held.");
                    pausedLogged = true;
                }
                else pausedLogged = false;
                foreach (var a in actions)
                {
                    if (ct.IsCancellationRequested) break;
                    await Execute(a, fg, ct);
                }
                }
                finally { _gate.Release(); }
                Ticked?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Emit("Error: " + ex.Message); }
            try { await Task.Delay(tick, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private const ushort ShiftVk = 0xA0; // VK_LSHIFT = Roblox's default Shift Lock key
    /// <summary>Shift lock state from the camera: true/false = certain, null = unreadable (animation, ambiguous camera offset).</summary>
    private bool? _shiftLockMem;
    private bool? _camFollow;
    private bool _camFree;
    private CancellationTokenSource? _turnCts;

    // v0.2.72: automatic shift lock. Round (InRound) → on; other screens (vote, results, lobby, menu) → off so GUI clicks land.
    // v0.2.80 (no guessing): Shift is only pressed when the state is read with certainty from the camera, and the result is not assumed:
    // the next tick reads it again. If the state still does not match 2.5 s after pressing → counted as a failure; 3 failures → stop on this screen.
    private long _slLastPress = -10_000;
    private int _slFails;
    private bool? _slWant;
    private GameScreen _slScreen = GameScreen.Unknown;

    /// <summary>v0.3.12: live enemies are read and the screen is not the lobby/lobby menu (Shift is forbidden in the lobby).</summary>
    public static bool MustShiftLock(GameSnapshot s) => MustShiftLock(s.Screen, s.EnemyCount);
    public static bool MustShiftLock(GameScreen screen, int enemyCount) =>
        enemyCount > 0 && screen is not (GameScreen.Lobby or GameScreen.LobbyMenu or GameScreen.QueueWaiting or GameScreen.TrialsMenu or GameScreen.CodesMenu);

    private long _slGiveUpLogAt = -60_000;

    private async Task EnsureShiftLock(GameScreen screen, IReadOnlyList<MacroAction> actions, long now, CancellationToken ct, bool mustLock = false)
    {
        if (screen != _slScreen) { _slScreen = screen; _slFails = 0; _slWant = null; }
        // Shift lock state only from the camera (no guessing): unreadable / animated camera → do not press Shift.
        if (screen == GameScreen.CodesMenu) return; // v0.3.26: never Shift while typing a code (lobby)
        if ((screen == GameScreen.Unknown && !mustLock) || _camFree || _shiftLockMem is not { } cur) return;
        // GUI click this tick: click handling turns shift lock off & back on by itself.
        bool clicking = actions.Any(a => a is ClickAction or ScrollAction);
        bool want = (mustLock || screen == GameScreen.InRound) && !clicking;
        if (cur == want) { if (_slWant == want) _slFails = 0; _slWant = null; return; }
        if (now - _slLastPress < 2500) return;
        if (_slFails >= 3)
        {
            // Enemies present → never give up; retry every 6 s. No enemies → stop on this screen (old behavior).
            if (!(mustLock && want) || now - _slLastPress < 6000) return;
            if (now - _slGiveUpLogAt > 60_000) { _slGiveUpLogAt = now; Emit("Shift lock still off although enemies are present → retrying every 6 s (check Roblox Settings: Shift Lock Switch = On)."); }
        }
        else if (_slWant == want && ++_slFails >= 3)
        {
            Emit($"Shift lock did not change to {(want ? "on" : "off")} after 3× Shift (check Roblox Settings: Shift Lock Switch){(mustLock && want ? " → enemies present, still retrying every 6 s." : " → stopped trying on this screen.")}");
            if (!(mustLock && want)) return;
        }
        _slWant = want;
        _slLastPress = now;
        input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
        Emit(want ? (mustLock ? "Enemies detected → Shift pressed (shift lock must be on, verified from the camera)." : "Round running → Shift pressed (turning shift lock on, verified from the camera).") : $"Outside a round ({screen}) → Shift pressed (turning shift lock off for GUI clicks).");
        await Task.Delay(120, ct);
    }

    /// <summary>Smooth camera turn: the delta is split into small steps every ~8 ms over one tick, in the background.</summary>
    private long _recenterLogAt = -10_000;

    /// <summary>Cursor is inside the Roblox client area (2 px margin) and Roblox is in the foreground.</summary>
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
        if (now - _recenterLogAt > 5000) { _recenterLogAt = now; Emit($"Cursor left the Roblox window during {why} → pulled back to the center, click cancelled."); }
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
                // v0.2.76: shift lock released (e.g. after Ultimate) → relative movement drags the cursor out of the window. Stop & pull it back.
                if (!CursorInside()) { RecenterCursor("camera turn"); break; }
                try { await Task.Delay(8, cts.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }

    /// <summary>Cursor is at client point (x,y) ±3 px.</summary>
    private bool CursorAt(double x, double y) => input.CursorClient() is { } c && Math.Abs(c.X - x) <= 3 && Math.Abs(c.Y - y) <= 3;

    private async Task ClickVerified(ClickAction c, CancellationToken ct)
    {
        _turnCts?.Cancel();
        bool toggled = false;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            input.MoveClient(c.X, c.Y);
            await Task.Delay(90, ct);
            if (!input.IsTargetForeground()) { Emit($"Click {c.Reason} cancelled: Roblox is not in the foreground."); break; }
            if (CursorAt(c.X, c.Y))
            {
                Emit($"Click {c.Reason} ({c.X:0},{c.Y:0})");
                input.ClickClient(c.X, c.Y);
                break;
            }
            if (attempt == 1)
            {
                Emit($"Click {c.Reason} cancelled: cursor could not be placed at ({c.X:0},{c.Y:0}) (Roblox keeps pulling it).");
                break;
            }
            Emit($"Cursor pulled to the center (shift lock on) → Shift, then re-check before clicking {c.Reason}.");
            input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
            toggled = true;
            await Task.Delay(150, ct);
        }
        if (toggled && c.InRound)
        {
            // Restore the play style (shift lock on) inside the stage; the result is re-read from the camera next tick.
            await Task.Delay(150, ct);
            input.KeyDown(ShiftVk); await Task.Delay(40, ct); input.KeyUp(ShiftVk);
        }
    }

    private async Task Execute(MacroAction a, bool foreground, CancellationToken ct)
    {
        switch (a)
        {
            case NoteAction n:
                Emit(n.Reason);
                return;
            case StopAction s:
                Emit(s.Reason);
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
                    Emit($"Click {c.Reason} cancelled: ({c.X:0},{c.Y:0}) is outside the Roblox window {size.W}x{size.H}.");
                    break;
                }
                // v0.2.80 (no guessing): does not depend on the shift lock state. The cursor is moved to the target, then its position is
                // re-read from Windows: still on target = the click will land; pulled to the center by Roblox = shift lock on → Shift, verify again.
                // 2 failures → click cancelled.
                await ClickVerified(c, ct);
                break;
            case ScrollAction sc:
                Emit($"Scroll {sc.Reason} ({sc.X:0},{sc.Y:0}) x{Math.Abs(sc.Notches)}");
                input.ScrollClient(sc.X, sc.Y, sc.Notches);
                break;
            case AimAction or TurnAction when _camFree:
                break; // v0.2.77: camera is being moved by an animation → screen direction is invalid
            // v0.2.80: cursor aim & camera drag only when shift lock is CERTAINLY off; relative turn only when CERTAINLY on.
            case AimAction or TurnAction { Drag: true } when _shiftLockMem != false:
                break;
            case TurnAction { Drag: false } when _shiftLockMem != true:
                break;
            case AimAction am:
                if (input.ClientSize() is { } sz && am.X >= 0 && am.Y >= 0 && am.X < sz.W && am.Y < sz.H)
                    input.MoveClient(am.X, am.Y);
                break;
            case TurnAction { Drag: true } t:
                // Without shift lock: hold right-click, move relatively in steps, then release (the Roblox camera turns along).
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
                if (!CursorInside()) RecenterCursor("camera drag");
                break;
            case TurnAction t:
                SmoothTurn(t.Dx, t.Dy, (int)(Math.Clamp(settings().General.TickMs, 30, 2000) * 0.9));
                break;
            case AttackAction:
                // v0.2.76: click at the current cursor position only when the cursor is inside the Roblox window. After Ultimate the cursor
                // was once dragged outside and the basic attack clicked the browser/File Explorer (Byzan's report 24 Oct 23:24).
                if (!CursorInside()) { RecenterCursor("attack"); break; }
                input.ClickHere();
                break;
            case TypeTextAction tt:
                Emit($"Typing {tt.Reason} ({(tt.Unicode ? "unicode" : "keys")}).");
                input.KeyDown(0x11); await Task.Delay(30, ct); input.KeyDown(0x41); await Task.Delay(30, ct); input.KeyUp(0x41); input.KeyUp(0x11); // Ctrl+A
                await Task.Delay(40, ct);
                input.KeyDown(0x08); await Task.Delay(30, ct); input.KeyUp(0x08); // Backspace
                await Task.Delay(60, ct);
                foreach (char ch in tt.Text)
                {
                    if (!input.IsTargetForeground()) break;
                    if (tt.Unicode) input.TypeUnicode(ch);
                    else if (char.IsAsciiLetterOrDigit(ch)) { ushort vk = char.ToUpperInvariant(ch); input.KeyDown(vk); await Task.Delay(20, ct); input.KeyUp(vk); }
                    await Task.Delay(35, ct);
                }
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

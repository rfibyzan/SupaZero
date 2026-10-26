using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public enum MacroPhase { Idle, WalkingToLobby, Lobby, Voting, Fighting, Navigating, Upgrading, Advancing, Retrying, Done }

/// <summary>
/// AutoFarm brain: pure (no I/O), takes a <see cref="GameSnapshot"/> + time and returns actions. Easy to test.
/// Flow: lobby (walk to the Create Room circle → pick mode/map/act/difficulty → Start) → vote Yes → round (attack +
/// skills; area cleared → walk to the next area, attack rubble when stuck) → EndScreen: victory (upgrade if needed
/// → Next/Retry/Leave depending on mode) / defeat (Retry up to the limit).
/// </summary>
public sealed partial class FarmMachine(Func<AppSettings> settings)
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
    private double? _lastHp;
    private int _afterWinClicks;
    /// <summary>Map/act currently being played (from the Create Room menu).</summary>
    public string CurrentMap => _selectedMap;
    public int CurrentAct => _selectedAct;
    private bool _actAdvancePending;
    public int Retries => _retries;
    public string Status { get; private set; } = "Idle";

    public void Reset()
    {
        _pcStep = 0; _pcStepAt = null; _pcOpenAt = null;
        _waveArea = -1;
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
        _upClosing = false; _upOpenAt = _upCloseAt = null; _upSlotKey = null; _upSlotTries = _upBuyFails = 0;
        _lastScreen = GameScreen.Unknown;
        _stuckRef = null;
        StagesCleared = 0;
        _roundStart = null; _lastHp = null; _afterWinClicks = 0;
        Phase = MacroPhase.Idle;
        Status = "Idle";
        Calibrating = false;
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
                    _roundStart is long rs ? TimeSpan.FromMilliseconds(nowMs - rs) : null, StagesCleared, s.ResultText, s.Drops,
                    Matches(S.Farm.Mode, nameof(FarmMode.Infinite))));
                Diagnostic?.Invoke($"EndScreen: '{s.ResultText ?? "?"}' · last HP {(_lastHp is double lh ? lh.ToString("P0") : "?")} · " +
                    $"Next {(s.NextButton is { } n1 ? $"({n1.CenterX:0},{n1.CenterY:0})" : "-")} · Retry {(s.RetryButton is { } r1 ? $"({r1.CenterX:0},{r1.CenterY:0})" : "-")} · " +
                    $"Leave {(s.LeaveButton is { } l1 ? $"({l1.CenterX:0},{l1.CenterY:0})" : "-")} · mode {S.Farm.Mode}, after win {S.Farm.AfterWin}.");
                _roundStart = null;
            }
        }
        _lastScreen = screen;
        if (s.Hud is not null) _lastHud = s.Hud;
        if (screen == GameScreen.InRound && s.PlayerHp is double hpNow)
        {
            // Diagnostic log: HP drops below 50% / 25%, and at 0.
            foreach (var th in new[] { 0.5, 0.25, 0.0001 })
                if (_lastHp is double prev && prev >= th && hpNow < th)
                    Diagnostic?.Invoke($"HP {hpNow:P0} ({Wave(s)}{s.Enemies.Count(e => e.Alive)} enemies alive" +
                        (s.PlayerPos is { } pp ? $", at ({pp.X:0},{pp.Z:0})" : "") + (s.BossName is { Length: > 0 } bn ? $", boss {bn}" : "") + ").");
            _lastHp = hpNow;
        }

        var actions = Calibrating ? CalibrationStep(s, S, nowMs) : screen switch
        {
            GameScreen.Unknown => Wait("Waiting for Roblox / DataModel."),
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
        // Release movement keys in every phase that is not walking.
        if (Phase is not (MacroPhase.WalkingToLobby or MacroPhase.Navigating) && !actions.Any(a => a is MoveAction))
            return [new MoveAction([], "Stop walking"), .. actions];
        return actions;
    }

    private void OnScreenChanged(GameScreen from, GameScreen to)
    {
        if (to == GameScreen.Victory)
        {
            StagesCleared++;
            if (_selectedAct > 0 && _selectedMap.Length > 0 && !Matches(settings().Farm.Mode, nameof(FarmMode.Infinite)))
            {
                ActCleared?.Invoke(_selectedMap, _selectedAct);
                _actAdvancePending = true; // v0.3.5: act only advances when Next is clicked (Raid/Retry: act stays; log 25 Oct "Act 212")
            }
        }
        if (to == GameScreen.StartVote || (to == GameScreen.InRound && from is GameScreen.Victory or GameScreen.Defeat))
        {
            _boughtThisStage = 0;
            _upgradeDoneThisStage = false;
            _pendingBuyConfirm = false;
        }
        if (to == GameScreen.Victory) _retries = 0;
        _afterWinClicks = 0;
        if (to == GameScreen.Defeat) Defeats++;
        if (to is not (GameScreen.LobbyMenu or GameScreen.TrialsMenu)) { _diffAt = null; _scrolls.Clear(); _targetTries.Clear(); }
        _stuckRef = null;
    }

    /// <summary>Room waiting screen: click "Skip Waiting" when free; when it costs (Uses 1 Crystal), wait for the countdown.</summary>
    private IReadOnlyList<MacroAction> QueueWait(GameSnapshot s, long now)
    {
        if (s.QueueSkip is not { } r) return [];
        if (s.QueueSkipCosts)
            return Note("queue-cost", now, 8000, "Skip Waiting costs 1 Crystal → not clicked, waiting for the countdown.");
        if (!InWindow(s, r.CenterX, r.CenterY))
            return Note("queue-out", now, 5000, $"Skip Waiting button is outside the window ({r.CenterX:0},{r.CenterY:0}).");
        if (!Ready("queue-skip", now, 2000)) return [];
        Status = "Waiting for room, clicking Skip Waiting.";
        return [new ClickAction(r.CenterX, r.CenterY, "Skip Waiting")];
    }

    private List<MacroAction> Wait(string why)
    {
        Phase = MacroPhase.Idle;
        Status = why;
        return [];
    }


    private List<MacroAction> Stop(string why)
    {
        Phase = MacroPhase.Done;
        Status = why;
        return [new MoveAction([], "Stop"), new StopAction(why)];
    }

    private List<MacroAction> Note(string key, long now, int everyMs, string text) =>
        Ready("note:" + key, now, everyMs) ? [new NoteAction(text)] : [];

    /// <summary>
    /// Mouse delta X to turn the camera yaw from direction (lx,lz) to (tx,tz). Mouse right = camera right.
    /// 2° tolerance, half the error per tick, max 80 px per tick to avoid overshoot.
    /// </summary>
    public static int Turn(double lx, double lz, double tx, double tz, double pxPerDeg, double gain = 0.5, double deadzone = 2, int max = 80)
    {
        if (Math.Abs(lx) + Math.Abs(lz) < 1e-6 || Math.Abs(tx) + Math.Abs(tz) < 1e-6) return 0;
        // Clockwise angle seen from above (Roblox: right = (-lz, lx) for look (lx, lz)).
        double cross = lx * tz - lz * tx, dot = lx * tx + lz * tz;
        double deg = Math.Atan2(cross, dot) * 180 / Math.PI; // positive = target on the right
        if (Math.Abs(deg) < deadzone) return 0;
        // Proportional control (default half the error per tick) for smooth turning without overshoot.
        return (int)Math.Clamp(Math.Round(deg * gain * Math.Max(0.5, pxPerDeg)), -max, max);
    }

    /// <summary>
    /// Mouse delta Y for camera pitch: mouse down = camera looks down. Target pitch = direction to the enemy, clamped to -40°..+10°
    /// so the camera never looks up at the ceiling.
    /// </summary>
    public static int Pitch(Vec3 look, double dx, double dy, double dz, double pxPerDeg, double gain = 0.5, double deadzone = 3, int max = 60)
    {
        double ll = Math.Sqrt(look.X * look.X + look.Z * look.Z), tl = Math.Sqrt(dx * dx + dz * dz);
        if (ll < 1e-6 && Math.Abs(look.Y) < 1e-6) return 0;
        double cur = Math.Atan2(look.Y, ll) * 180 / Math.PI;
        double want = Math.Clamp(tl < 1e-6 ? -80 : Math.Atan2(dy, tl) * 180 / Math.PI, -80, 10); // -80°: enemies cluster below while the player is airborne
        double err = cur - want; // positive = camera looking up too much → mouse down
        if (Math.Abs(err) < deadzone) return 0;
        return (int)Math.Clamp(Math.Round(err * gain * Math.Max(0.5, pxPerDeg)), -max, max);
    }

    private static bool IsDash(SkillBinding k) => k.Name == "Dash";
    private static bool IsUlt(SkillBinding k) => k.Name == "Ultimate";
    private static bool IsSwap(SkillBinding k) => k.Name is "Swap power" or "Ganti power" or "Ganti senjata"; // v0.3.16: legacy Indonesian names from older settings.json

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
    /// <summary>Called once when the screen changes to Victory/Defeat (for the webhook).</summary>
    public Action<Notify.RoundReport>? RoundEnded { get; set; }
    /// <summary>Diagnostic log (HP drops, EndScreen contents) without changing the action list.</summary>
    public Action<string>? Diagnostic { get; set; }

    /// <summary>Act number from text like "Act 3", "Training [Act 1]", "Act 1 Training". 0 = none.</summary>
    public static int ActNumber(string? text)
    {
        if (text is null) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"Act\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>Called on victory: records the cleared act (for "All acts" mode).</summary>
    public Action<string, int>? ActCleared { get; set; }

    private static bool Matches(string? actual, string want) =>
        actual is not null && want.Length > 0 &&
        actual.Replace(" ", "").Contains(want.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
}

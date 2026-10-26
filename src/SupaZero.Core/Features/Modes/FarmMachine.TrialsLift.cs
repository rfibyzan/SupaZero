using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>Press E within this distance of the Lift (prompt MaxActivationDistance 14 in snapshot World 09:31:14).</summary>
    internal const double LiftPressDist = 6;
    internal const long LiftPressEveryMs = 2000;
    private long _liftPressAt = -LiftPressEveryMs;

    /// <summary>v0.3.18 Trials arena (no Areas, circular Barriers, Lift at the center). Floor still running (prompt disabled) → wait at spawn
    /// for the next wave. The game enables "Ascend" → walk to the Lift and tap E (HoldDuration 0) to go up to the next floor.</summary>
    private List<MacroAction> TrialsLift(GameSnapshot s, LiftInfo lift, long now, AppSettings S)
    {
        Phase = MacroPhase.Navigating;
        if (!lift.Enabled) { Status = "Trials: floor in progress, waiting for the next wave."; return [new MoveAction([], "Wait for wave")]; }
        if (s.PlayerPos is not { } me) { Status = "Trials: Lift open, player position unreadable."; return [new MoveAction([], "Wait")]; }
        double d = Navigator.Distance2D(me.X, me.Z, lift.Pos.X, lift.Pos.Z);
        double reach = Math.Min(LiftPressDist, lift.MaxDistance - 1);
        if (d > reach)
        {
            var walk = WalkPath(s, lift.Pos, Math.Max(2, reach - 2), now, "Lift", S.Navigation.AttackWhenStuck);
            Status = $"Trials floor cleared → walking to the Lift ({d:0} stud).";
            if (walk.Count > 0) return walk;
        }
        Status = $"Trials: {lift.Action} via the Lift (E).";
        if (now - _liftPressAt < LiftPressEveryMs) return [new MoveAction([], "Wait for Lift")];
        _liftPressAt = now;
        return [new MoveAction([], "Stand at the Lift"), new KeyAction(VirtualKeys.E, 80, $"{lift.Action} (Lift, E)")];
    }
}

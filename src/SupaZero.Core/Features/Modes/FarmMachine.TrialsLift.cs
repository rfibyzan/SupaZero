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
        string who = lift.Elevator ? "Elevator" : "Trials";
        string name = lift.Elevator ? "elevator" : "Lift";
        if (!lift.Enabled)
        {
            if (!lift.Elevator) { Status = "Trials: floor in progress, waiting for the next wave."; return [new MoveAction([], "Wait for wave")]; }
            // v0.3.42: the HUD asks for the elevator but its prompt is not on (yet) → walk there and wait, never press E.
            _pending.AddRange(Note("elevator-off", now, 10000, "HUD says 'Head to the elevator' but ExitPrompt is disabled → walking to the elevator, not pressing E."));
        }
        if (s.PlayerPos is not { } me) { Status = $"{who}: {name} open, player position unreadable."; return [new MoveAction([], "Wait")]; }
        double d = Navigator.Distance2D(me.X, me.Z, lift.Pos.X, lift.Pos.Z);
        double reach = Math.Min(LiftPressDist, lift.MaxDistance - 1);
        if (d > reach && lift.Elevator && Math.Abs(me.Y - lift.Pos.Y) >= 8 && ElevatorStairs(s, lift, me, now, S) is { } up) return up;
        if (d > reach)
        {
            var walk = WalkPath(s, lift.Pos, Math.Max(2, reach - 2), now, "Lift", S.Navigation.AttackWhenStuck);
            Status = lift.Elevator ? $"Floor cleared → walking to the elevator ({d:0} stud)." : $"Trials floor cleared → walking to the Lift ({d:0} stud).";
            if (walk.Count > 0) return walk;
        }
        if (!lift.Enabled) { Status = "Elevator: waiting for its prompt."; return [new MoveAction([], "Wait at elevator")]; }
        Status = $"{who}: {lift.Action} via the {name} (E).";
        if (now - _liftPressAt < LiftPressEveryMs) return [new MoveAction([], $"Wait for {name}")];
        _liftPressAt = now;
        int holdMs = lift.Hold > 0 ? (int)(lift.Hold * 1000) + 250 : 80; // v0.3.42: hold prompts need HoldDuration + 0.25 s
        return [new MoveAction([], $"Stand at the {name}"), new KeyAction(VirtualKeys.E, holdMs, $"{lift.Action} ({name}, E)")];
    }

    /// <summary>v0.3.43: elevator on another floor (Spirit's Bathtub floor 2, Exit Y 98 vs floor Y≈20) → follow the recorded stair route.
    /// Ground floor away from the route → A* to its first point. Unknown elevator or off the route mid-air = stand still (never guess).</summary>
    private List<MacroAction>? ElevatorStairs(GameSnapshot s, LiftInfo lift, Vec3 me, long now, AppSettings S)
    {
        if (ElevatorRoutes.For(lift.Pos) is not { } route)
        {
            Status = "Elevator is on another floor and no route is known → waiting.";
            return [new MoveAction([], "No stair route"), .. Note("elevator-noroute", now, 10000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Elevator at ({lift.Pos.X:0},{lift.Pos.Y:0},{lift.Pos.Z:0}) is {lift.Pos.Y - me.Y:0} studs above/below the player and has no recorded route."))];
        }
        int i = ElevatorRoutes.Next(route, me);
        if (i >= route.Points.Count) return null; // top reached → normal walk/press
        if (i < 0)
        {
            var first = route.Points[0];
            if (Math.Abs(me.Y - first.Y) < 6)
            {
                Status = $"Floor cleared → walking to the stairs ({route.Name}).";
                var toStairs = WalkPath(s, first, 2, now, "stairs", S.Navigation.AttackWhenStuck);
                // v0.3.45: arrived at the foot → never fall back to walking toward the elevator's x/z (caused back-and-forth at the foot).
                return toStairs.Count > 0 ? toStairs : [new MoveAction([], "At the stairs")];
            }
            Status = "Elevator: off the stair route, position unknown → waiting.";
            return [new MoveAction([], "Off route"), .. Note("elevator-offroute", now, 10000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Player ({me.X:0},{me.Y:0},{me.Z:0}) is not near the {route.Name} route."))];
        }
        var wp = route.Points[i];
        var walk = WalkTo(s, wp, 1.5, now, $"stairs {i + 1}", S.Navigation.AttackWhenStuck);
        Status = $"Climbing the stairs to the elevator ({i + 1}/{route.Points.Count}, {Navigator.Distance2D(me.X, me.Z, wp.X, wp.Z):0} stud).";
        return walk.Count > 0 ? walk : [new MoveAction([], "Stair point")];
    }
}

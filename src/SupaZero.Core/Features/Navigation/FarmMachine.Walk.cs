using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    private readonly PathPlanner _planner = new();
    private List<Vec3>? _path;
    private Vec3? _pathGoal;
    private long _pathAt;
    private int _enterArea, _enterDepth;
    private long _enterNearSince;

    /// <summary>
    /// Destination for "Enter area N". If we reached the entry point but the HUD still says "Enter area N" (the game trigger is deeper,
    /// e.g. Hxh Area 2 stops ~31 studs from the marker), advance step by step 15 studs every 1.5 s toward the center of the Areas.N box.
    /// </summary>
    private Settings.Vec3? EnterTarget(GameSnapshot s, AppSettings S, long now)
    {
        if (s.NextAreaPos is not { } baseP) return null;
        var hud = s.Hud;
        int n = hud?.TargetArea ?? 0;
        if (n != _enterArea || hud is not { Enter: true }) { _enterArea = n; _enterDepth = 0; _enterNearSince = 0; }
        Settings.Vec3 Target()
        {
            if (_enterDepth == 0 || s.NextAreaCenter is not { } c) return baseP;
            double dx = c.X - baseP.X, dz = c.Z - baseP.Z, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1) return baseP;
            double t = Math.Min(len, _enterDepth * 15.0) / len;
            return new(baseP.X + dx * t, baseP.Y, baseP.Z + dz * t);
        }
        var tgt = Target();
        if (hud is { Enter: true } && s.PlayerPos is { } p)
        {
            double d = Math.Sqrt((p.X - tgt.X) * (p.X - tgt.X) + (p.Z - tgt.Z) * (p.Z - tgt.Z));
            if (d <= S.Navigation.ArriveRadius + 3)
            {
                if (_enterNearSince == 0) _enterNearSince = now;
                else if (now - _enterNearSince >= 1500) { _enterDepth++; _enterNearSince = 0; _path = null; tgt = Target(); }
            }
            else _enterNearSince = 0;
        }
        return tgt;
    }
    private string? _pathNote;

    /// <summary>
    /// Walk to the destination along a path, not a straight line: if the straight line is blocked by obstacles or leaves the area, A* finds a route
    /// (e.g. a U-turn detour), then the character walks from waypoint to waypoint. No map / path → <see cref="WalkTo"/>.
    /// </summary>
    private List<MacroAction> WalkPath(GameSnapshot s, Vec3 target, double radius, long now, string label, bool attackWhenStuck)
    {
        if (s.Nav is not { IsEmpty: false } nav || s.PlayerPos is not { } p
            || Navigator.Distance2D(p.X, p.Z, target.X, target.Z) <= Math.Max(radius, 6))
        { _path = null; return WalkTo(s, target, radius, now, label, attackWhenStuck); }
        bool stuck = _stuckRef is not null && now - _stuckSince >= 1500;
        bool goalMoved = _pathGoal is not { } g || Navigator.Distance2D(g.X, g.Z, target.X, target.Z) > 6;
        // Stuck on an object that is not on the map: mark a point ~2.5 studs ahead (toward the waypoint) as an obstacle, then re-plan.
        if (stuck && _path is { Count: > 0 } cur && now - _pathAt > 1500)
        {
            var w0 = cur[0];
            double d = Math.Max(0.01, Navigator.Distance2D(p.X, p.Z, w0.X, w0.Z));
            _planner.Block(p.X + (w0.X - p.X) / d * 2.5, p.Z + (w0.Z - p.Z) / d * 2.5);
        }
        if (_path is null || goalMoved || stuck && now - _pathAt > 1500 || now - _pathAt > 4000)
        {
            _pathGoal = target;
            _pathAt = now;
            bool clear = _planner.Clear(nav, p, target);
            _path = clear ? [target] : _planner.Find(nav, p, target);
            // v0.3.9: path failed/partial → drop old stuck marks and search once more.
            if (!clear && (_path is null || _planner.Partial) && _planner.ClearBlocks()) _path = _planner.Find(nav, p, target);
            // Diagnostics (max 1x / 10 s): position, destination, path type, map obstacle count → can be matched against a snapshot.
            _pending.AddRange(Note("path-diag", now, 10000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Path to {label}: from ({p.X:0},{p.Z:0}) to ({target.X:0},{target.Z:0}), {(clear ? "straight" : _path is null ? "not found" : $"{_path.Count} points{(_planner.UsedOutside ? ", via outside Areas" : "")}{(_planner.Partial ? ", partial" : "")}")}; map {nav.Walkable.Count} areas, {nav.Solids?.Count ?? 0} obstacles.")));
            if (_path is null || _planner.Partial)
                _pending.AddRange(Note("border-diag", now, 15000, $"Border/Blocker: {nav.BorderDiag ?? "?"}. HUD: \"{s.HudRaw ?? "-"}\"."));
            if (_path is null && OtherLevel(nav, p, target))
            {
                // v0.3.10 (ER, snapshot World 07:43:42): the A* grid is single-floor. ER spawn is on floor Y≈240, arena Areas.1 at Y≈278
                // via stairs (sloped StairCollision); from the lower floor the stair mesh/arena cylinder = "wall" → pathing always fails.
                // Destination on another floor → walk straight as before v0.3.9 (the ER route really is straight up the stairs).
                return [.. WalkTo(s, target, radius, now, label, attackWhenStuck), .. Note("path-level", now, 8000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Path to {label}: destination on another floor (feet Y {PathPlanner.FootY(nav, p):0}, destination Y {target.Y:0}) → walking straight (up the stairs)."))];
            }
            if (_path is null && RubbleStraight(label, p, target))
                return [.. WalkTo(s, target, radius, now, label, true), .. Note("path-rubble", now, 8000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Path to rubble not found ({_planner.LastFailure ?? "?"}), rubble {Navigator.Distance2D(p.X, p.Z, target.X, target.Z):0} studs away → walking straight into the debris, attacking when stuck."))];
            if (_path is null)
            {
                // v0.3.9 (Byzan 25 Oct 10:46): previously "walk straight" = bumping into buildings already read as obstacles. Now stand still & re-plan.
                Status = $"Path to {label} not found, re-planning…";
                return [new MoveAction([], "Waiting for path"), .. Note("path-none", now, 8000, $"Path to {label} not found: {_planner.LastFailure ?? "?"}. Not walking straight through obstacles; re-planning.")];
            }
            if (_planner.Partial) _pathNote = $"Path to {label}: {_planner.LastFailure}.";
            else if (_planner.UsedOutside && _path.Count > 1) _pathNote = $"Path to {label} detours via roads outside the Areas boxes ({_path.Count} waypoints).";
        }
        // Drop waypoints already passed (≤ 3 studs) or whose next point is already in line of sight.
        while (_path.Count > 1 && Navigator.Distance2D(p.X, p.Z, _path[0].X, _path[0].Z) <= 3) _path.RemoveAt(0);
        var wp = _path[0];
        bool last = _path.Count == 1;
        var walk = WalkTo(s, wp, last ? radius : 1.5, now, last ? label : $"{label} (turn {_path.Count - 1})", attackWhenStuck);
        // Partial path reached its end: the rest is walked straight (e.g. destroy rubble/Blocker by attacking while stuck).
        // v0.3.9: end of a partial path → do not walk straight into buildings/Blocker (rubble has its own approach); re-plan.
        if (walk.Count == 0 && last && _planner.Partial && OtherLevel(nav, p, target)) return WalkTo(s, target, radius, now, label, attackWhenStuck);
        if (walk.Count == 0 && last && _planner.Partial && RubbleStraight(label, p, target)) return WalkTo(s, target, radius, now, label, true);
        if (walk.Count == 0 && last && _planner.Partial) { _path = null; Status = $"End of partial path to {label}, re-planning…"; return [new MoveAction([], "Waiting for path")]; }
        if (_pathNote is { } pn) { _pathNote = null; walk = [.. walk, .. Note("path-partial", now, 8000, pn)]; }
        if (walk.Count == 0 && !last) { _path.RemoveAt(0); return WalkPath(s, target, radius, now, label, attackWhenStuck); }
        if (walk.Count == 0) _path = null;
        return walk;
    }

    /// <summary>
    /// v0.3.31 (Byzan 27 Oct 23:23, West City Act 1 "Break the rubble to continue!", rubble 41 studs, character standing still): West City
    /// Border*.Rubble has no ClimbStop, only debris meshes (BLD_DEBRIS/DIRT_MOUND, RIE World 23:14:02) around the Blocker, so A* finds no
    /// cell near the rubble ("Path to rubble not found: no reachable cell", log 23:08:43) and the macro waited forever. Rubble ≤ 60 studs:
    /// walk straight into the debris; stuck = jump + attack (and Fight treats a stuck rubble approach as in range).
    /// </summary>
    public const double RubbleStraightRange = 60;
    private static bool RubbleStraight(string label, Vec3 p, Vec3 target) =>
        label == "rubble" && Navigator.Distance2D(p.X, p.Z, target.X, target.Z) <= RubbleStraightRange;

    /// <summary>v0.3.10: destination ≥ 8 studs above/below the foot floor (e.g. the ER arena above the spawn stairs). Hxh: entry Y≈2, feet Y≈3.</summary>
    internal static bool OtherLevel(NavMap nav, Vec3 p, Vec3 target) => Math.Abs(target.Y - PathPlanner.FootY(nav, p)) >= 8;

    private List<MacroAction> WalkTo(GameSnapshot s, Vec3 target, double radius, long now, string label, bool attackWhenStuck)
    {
        if (s.PlayerPos is not { } p || s.CameraLook is not { } look)
        {
            Status = "Character/camera position unreadable.";
            return [new MoveAction([], "No position data"), .. Note("nopos", now, 6000, "Character or camera position unreadable (check offsets).")];
        }
        var keys = Navigator.KeysToward(p.X, p.Z, look.X, look.Z, target.X, target.Z, radius);
        if (keys.Count == 0) { _stuckRef = null; return []; }
        var actions = new List<MacroAction> { new MoveAction(keys, $"Walk to {label}") };
        if (_stuckRef is null || Navigator.Distance2D(p.X, p.Z, _stuckRef.X, _stuckRef.Z) > 1.5)
        {
            _stuckRef = p;
            _stuckSince = now;
        }
        else if (now - _stuckSince >= 1500)
        {
            if (Ready("jump", now, 1200)) actions.Add(new KeyAction(VirtualKeys.Space, 80, "Jump (stuck)"));
            if (attackWhenStuck && now - _lastAttack >= 150)
            {
                _lastAttack = now;
                actions.Add(new AttackAction("Attack obstacle"));
            }
        }
        Status = $"Walking to {label} ({Navigator.Distance2D(p.X, p.Z, target.X, target.Z):0} studs)";
        return actions;
    }

    // ---------- Non-Story modes ----------
}

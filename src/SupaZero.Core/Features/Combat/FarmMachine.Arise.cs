using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>ARISE corpse search radius; E-hold distance (prompt MaxActivationDistance 10); time limit per corpse.</summary>
    internal const double AriseRadius = 40, AriseHoldDist = 4;
    internal const long AriseGiveUpMs = 8000;
    private readonly Dictionary<string, long> _ariseBan = new(StringComparer.Ordinal);
    private string? _ariseKey;
    private long _ariseSince, _ariseLastHold;
    private int _ariseHolds;

    private int? _ariseShadowsBefore;
    private static string AriseKey(AriseInfo a) => $"{Math.Round(a.Pos.X)},{Math.Round(a.Pos.Z)}";

    /// <summary>Nearest ARISE corpse: walk to it; within 4 studs → release movement + hold E. null = no corpse.</summary>
    private List<MacroAction>? Arise(GameSnapshot s, long now, AppSettings S)
    {
        // v0.2.70: Shadows capacity full (e.g. 3/3) → do not waste time on corpses.
        if (s.PlayerPos is not { } me || s.Arises.Count == 0 || s.Shadows is { } sh && sh.Count >= sh.Max) { _ariseKey = null; return null; }
        // v0.2.79 (no guessing): the Shadows count must be readable so the Arise result can be verified (Count goes up).
        if (s.Shadows is not { } shNow) { _ariseKey = null; _pending.AddRange(Note("arise-noshadow", now, 15000, "Arise corpse present, but Shadows count unreadable → Arise skipped.")); return null; }
        if (_ariseKey is not null && _ariseShadowsBefore is int before && shNow.Count > before)
        {
            _ariseBan[_ariseKey] = now + 60_000;
            _pending.AddRange(Note("arise-ok", now, 1000, $"Arise succeeded: Shadows {before} → {shNow.Count}/{shNow.Max}."));
            _ariseKey = null; _ariseShadowsBefore = null;
            return null;
        }
        var best = s.Arises
            .Where(a => !(_ariseBan.TryGetValue(AriseKey(a), out long until) && until > now))
            .Select(a => (a, d: Navigator.Distance2D(me.X, me.Z, a.Pos.X, a.Pos.Z)))
            .Where(t => t.d <= AriseRadius && Math.Abs(t.a.Pos.Y - me.Y) <= 25)
            .OrderBy(t => t.d).FirstOrDefault();
        if (best.a is null) { _ariseKey = null; return null; }
        string key = AriseKey(best.a);
        if (_ariseKey != key) { _ariseKey = key; _ariseSince = now; _ariseHolds = 0; _ariseShadowsBefore = shNow.Count; }
        // Already held E 2× but the prompt is still there (e.g. Shadows capacity full) → skip for 60 s.
        else if (now - _ariseSince > AriseGiveUpMs || (_ariseHolds >= 2 && now - _ariseLastHold >= 1500))
        {
            _ariseBan[key] = now + 60_000;
            _ariseKey = null;
            return [new MoveAction([], "Skip Arise"), .. Note("arise-ban", now, 4000, $"Arise {best.a.Name} did not succeed within {AriseGiveUpMs / 1000} s, skipped.")];
        }
        Status = $"Arise {best.a.Name} ({best.d:0} stud)";
        if (best.d > AriseHoldDist)
        {
            var walk = WalkPath(s, best.a.Pos, AriseHoldDist - 1, now, $"corpse {best.a.Name}", false);
            if (walk.Count > 0) return walk;
        }
        // Already close: hold E once per 1.5 s; jump in between so we are not standing still.
        if (now - _ariseLastHold >= 1500)
        {
            _ariseLastHold = now;
            _ariseHolds++;
            int ms = (int)Math.Round(best.a.Hold * 1000) + 250;
            return [new MoveAction([], "Stand still briefly for Arise"), new KeyAction(VirtualKeys.E, ms, $"Arise {best.a.Name} (hold E {ms} ms)")];
        }
        return [new MoveAction([], "Wait for Arise"), new KeyAction(VirtualKeys.Space, 80, "Jump (waiting for Arise)")];
    }

    // ---------- Pathfinding (A* on the Areas map + Navigation obstacles) ----------
}

using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>Coin/health pickup radius when the area is clear; health radius when HP is low; low-HP threshold.</summary>
    internal const double PickupClearRadius = 90, PickupHealthRadius = 60, PickupNearRadius = 30, ClusterRadius = 15, LowHpPickup = 0.5;
    /// <summary>Items not picked up within this time (blocked/unreachable) are skipped.</summary>
    internal const long PickupGiveUpMs = 6000;
    private readonly Dictionary<string, long> _pickupBan = new(StringComparer.Ordinal);
    private string? _pickupKey;
    private long _pickupSince;

    private static string PickupKey(PickupInfo p) => $"{p.Kind}@{Math.Round(p.Pos.X)},{Math.Round(p.Pos.Z)}";

    /// <summary>Walk over the nearest item (coin/health) within the radius. null = no item to pick up.</summary>
    private List<MacroAction>? Pickup(GameSnapshot s, long now, double radius, bool healthOnly, AppSettings S)
    {
        if (s.PlayerPos is not { } me || s.Pickups.Count == 0) { _pickupKey = null; return null; }
        // Health is only picked up when HP is not full (the pickup is wasted when full).
        bool wantHealth = s.PlayerHp is not { } hpNow || hpNow < 0.95;
        var best = s.Pickups
            .Where(p => (healthOnly ? p.IsHealth : (!p.IsHealth || wantHealth)) && !(_pickupBan.TryGetValue(PickupKey(p), out long until) && until > now))
            .Select(p => (p, d: Navigator.Distance2D(me.X, me.Z, p.Pos.X, p.Pos.Z)))
            .Where(t => t.d <= radius && Math.Abs(t.p.Pos.Y - me.Y) <= 25)
            .OrderBy(t => t.d).FirstOrDefault();
        if (best.p is null) { _pickupKey = null; return null; }
        string key = PickupKey(best.p);
        if (_pickupKey != key) { _pickupKey = key; _pickupSince = now; }
        else if (now - _pickupSince > PickupGiveUpMs)
        {
            _pickupBan[key] = now + 60_000;
            _pickupKey = null;
            return [new MoveAction([], "Skip item"), .. Note("pickup-ban", now, 4000, $"Item {best.p.Kind} not picked up within {PickupGiveUpMs / 1000} s, skipped.")];
        }
        string what = best.p.IsHealth ? "health" : "coin";
        // Radius 2 (not 1): items are picked up on touch; radius 1 makes the character slowly jitter back and forth over the item.
        var walk = WalkPath(s, best.p.Pos, 2.0, now, what, false);
        int left = s.Pickups.Count;
        Status = $"Pick up {what} ({best.d:0} studs, {left} items)";
        return walk.Count > 0 ? walk : [new MoveAction([], $"On top of {what}")];
    }

    // ---------- Shadow Lord: Arise (v0.2.67) ----------
}

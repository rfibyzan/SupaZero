using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>Radius pungut koin/health saat area bersih; radius health saat darah rendah; ambang darah rendah.</summary>
    internal const double PickupClearRadius = 90, PickupHealthRadius = 60, PickupNearRadius = 30, ClusterRadius = 15, LowHpPickup = 0.5;
    /// <summary>Item yang tidak terambil dalam waktu ini (terhalang/tidak terjangkau) dilewati.</summary>
    internal const long PickupGiveUpMs = 6000;
    private readonly Dictionary<string, long> _pickupBan = new(StringComparer.Ordinal);
    private string? _pickupKey;
    private long _pickupSince;

    private static string PickupKey(PickupInfo p) => $"{p.Kind}@{Math.Round(p.Pos.X)},{Math.Round(p.Pos.Z)}";

    /// <summary>Jalan melewati item terdekat (koin/health) dalam radius. null = tidak ada item yang perlu diambil.</summary>
    private List<MacroAction>? Pickup(GameSnapshot s, long now, double radius, bool healthOnly, AppSettings S)
    {
        if (s.PlayerPos is not { } me || s.Pickups.Count == 0) { _pickupKey = null; return null; }
        // Health hanya dipungut bila darah tidak penuh (pickup terbuang bila penuh).
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
            return [new MoveAction([], "Lewati item"), .. Note("pickup-ban", now, 4000, $"Item {best.p.Kind} tidak terambil dalam {PickupGiveUpMs / 1000} dtk, dilewati.")];
        }
        string what = best.p.IsHealth ? "health" : "koin";
        // Radius 2 (bukan 1): item terambil saat tersentuh; radius 1 bikin karakter maju-mundur pelan di atas item.
        var walk = WalkPath(s, best.p.Pos, 2.0, now, what, false);
        int left = s.Pickups.Count;
        Status = $"Ambil {what} ({best.d:0} stud, {left} item)";
        return walk.Count > 0 ? walk : [new MoveAction([], $"Di atas {what}")];
    }

    // ---------- Shadow Lord: Arise (v0.2.67) ----------
}

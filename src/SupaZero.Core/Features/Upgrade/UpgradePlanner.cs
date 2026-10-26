using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>Pick the next SkillTree node to buy according to column priority.</summary>
public static class UpgradePlanner
{
    /// <summary>
    /// Buyable node = CanBuy shown ("!"), visible (Rect), not locked, price read and ≤ tokens (v0.2.92: no guessing,
    /// unread price/tokens = not bought). Per column the topmost node (smallest Index) is taken.
    /// </summary>
    public static SkillSlot? Next(IReadOnlyList<SkillSlot> slots, IReadOnlyList<string> priority, UpgradeMode mode, int boughtThisStage, int? tokens = null)
    {
        var buyable = slots.Where(s => s.CanBuy && s.Rect is not null && !s.Locked && s.Price is int p && tokens is int t && p <= t).ToList();
        if (buyable.Count == 0) return null;
        var order = priority.Where(c => UpgradeSettings.Columns.Contains(c)).Distinct().ToList();
        if (order.Count == 0) order = [.. UpgradeSettings.Columns];

        if (mode == UpgradeMode.RoundRobin)
        {
            for (int i = 0; i < order.Count; i++)
            {
                string col = order[(boughtThisStage + i) % order.Count];
                var hit = buyable.Where(s => s.Column == col).OrderBy(s => s.Index).FirstOrDefault();
                if (hit is not null) return hit;
            }
            return null;
        }
        foreach (string col in order)
        {
            var hit = buyable.Where(s => s.Column == col).OrderBy(s => s.Index).FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }
}

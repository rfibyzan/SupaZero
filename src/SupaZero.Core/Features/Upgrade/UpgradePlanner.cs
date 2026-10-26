using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>Pilih node SkillTree berikutnya untuk dibeli sesuai prioritas kolom.</summary>
public static class UpgradePlanner
{
    /// <summary>
    /// Node layak beli = CanBuy tampil ("!"), terlihat (Rect), tidak terkunci, harga terbaca dan ≤ token (v0.2.92: tanpa tebakan,
    /// harga/token tak terbaca = tidak dibeli). Per kolom diambil node teratas (Index terkecil).
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

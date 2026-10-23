using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public class UpgradePlannerTests
{
    private static SkillSlot S(string col, int i, bool can = true) => new(col, i, can, new GuiRect(i, i, 10, 10));

    [Fact]
    public void Priority_FillsFirstColumnFirst()
    {
        var slots = new[] { S("CRIT", 0), S("ATK", 1), S("ATK", 0, false) };
        var next = UpgradePlanner.Next(slots, ["ATK", "CRIT"], UpgradeMode.Priority, 0)!;
        Assert.Equal(("ATK", 1), (next.Column, next.Index));
    }

    [Fact]
    public void RoundRobin_Alternates()
    {
        var slots = new[] { S("ATK", 0), S("CRIT", 0) };
        Assert.Equal("ATK", UpgradePlanner.Next(slots, ["ATK", "CRIT"], UpgradeMode.RoundRobin, 0)!.Column);
        Assert.Equal("CRIT", UpgradePlanner.Next(slots, ["ATK", "CRIT"], UpgradeMode.RoundRobin, 1)!.Column);
    }

    [Fact]
    public void NothingBuyable_Null() =>
        Assert.Null(UpgradePlanner.Next([S("ATK", 0, false)], ["ATK"], UpgradeMode.Priority, 0));
}

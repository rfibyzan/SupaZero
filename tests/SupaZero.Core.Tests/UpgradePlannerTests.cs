using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public class UpgradePlannerTests
{
    private static SkillSlot S(string col, int i, bool can = true, int price = 1) => new(col, i, can, new GuiRect(i, i, 10, 10), price);

    [Fact]
    public void Priority_FillsFirstColumnFirst()
    {
        var slots = new[] { S("CRIT", 0), S("ATK", 1), S("ATK", 0, false) };
        var next = UpgradePlanner.Next(slots, ["ATK", "CRIT"], UpgradeMode.Priority, 0, 99)!;
        Assert.Equal(("ATK", 1), (next.Column, next.Index));
    }

    [Fact]
    public void RoundRobin_Alternates()
    {
        var slots = new[] { S("ATK", 0), S("CRIT", 0) };
        Assert.Equal("ATK", UpgradePlanner.Next(slots, ["ATK", "CRIT"], UpgradeMode.RoundRobin, 0, 99)!.Column);
        Assert.Equal("CRIT", UpgradePlanner.Next(slots, ["ATK", "CRIT"], UpgradeMode.RoundRobin, 1, 99)!.Column);
    }

    [Fact]
    public void NothingBuyable_Null() =>
        Assert.Null(UpgradePlanner.Next([S("ATK", 0, false)], ["ATK"], UpgradeMode.Priority, 0));

    [Fact]
    public void TopNodeFirst_AndRespectsTokens()
    {
        var slots = new[] { S("DEF", 3, true, 9), S("DEF", 2, true, 5) };
        Assert.Equal(2, UpgradePlanner.Next(slots, ["DEF"], UpgradeMode.Priority, 0, 67)!.Index);
        Assert.Null(UpgradePlanner.Next(slots, ["DEF"], UpgradeMode.Priority, 0, 4));
        Assert.Null(UpgradePlanner.Next(slots, ["DEF"], UpgradeMode.Priority, 0, null));
    }
}

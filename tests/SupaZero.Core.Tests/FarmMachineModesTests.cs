using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    [Fact]
    public void Blessing_PicksByPriority_ThenRarity()
    {
        var offers = new List<BlessingOffer>
        {
            new("Chain Shatter", "RARE", "+50% splash", new GuiRect(390, 774, 352, 49)),
            new("Shard Snack", "RARE", "+7.0% heal", new GuiRect(787, 774, 352, 49)),
            new("Sharper Edge", "COMMON", "+35%", new GuiRect(1181, 774, 352, 49)),
        };
        Assert.Equal("Shard Snack", FarmMachine.ChooseBlessing(offers, ["Shard Snack", "Sharper Edge"])!.Name);
        Assert.Equal("Chain Shatter", FarmMachine.ChooseBlessing(offers, ["Big Heart"])!.Name);
        var (m, _) = New(s => s.Farm = s.Farm with { BlessingPriority = "Sharper Edge" });
        var a = Real(m.Step(new GameSnapshot { Attached = true, EnemyCount = 1, Blessings = offers, ClientW = 1920, ClientH = 1080 }, 0));
        var click = Assert.IsType<ClickAction>(Assert.Single(a));
        Assert.Equal(1181 + 176, click.X, 0);
    }

    [Fact]
    public void DropTarget_ClicksMaterialBeforeStart()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Map = "Hxh", Act = 1, Difficulty = Difficulty.Nightmare, DropTarget = "Fine Cloth" });
        var cards = new List<RewardCard>
        {
            new("Magic Pendant", true, false, new GuiRect(1225, 788, 118, 42), "27%"),
            new("Fine Cloth", true, false, new GuiRect(1450, 788, 118, 42), "100%"),
        };
        var snap = new GameSnapshot
        {
            Attached = true, LobbyMenuOpen = true, LobbyMapTag = "Hxh", LobbyActTag = "[Act 1]", LobbyDifficultyTag = "NIGHTMARE",
            LobbyRewards = cards, LobbyStart = Btn, ClientW = 1920, ClientH = 1009,
        };
        var a = Real(m.Step(snap, 0));
        Assert.Contains(a, x => x is ClickAction c && c.Reason.Contains("Fine Cloth"));
        var done = snap with { LobbyRewards = [cards[0], cards[1] with { Targeted = true }] };
        Assert.Contains(Real(m.Step(done, 5000)), x => x is ClickAction c && c.Reason.Contains("Start"));
    }
}

using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    [Fact]
    public void Victory_NonStoryMode_Retries()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Mode = "Infinite" });
        var a = m.Step(new GameSnapshot { Attached = true, EndScreenVisible = true, NextButton = Btn, RetryButton = new GuiRect(500, 500, 10, 10) }, 0);
        Assert.Equal("Retry", a.OfType<ClickAction>().Single().Reason);
    }

    [Fact]
    public void Victory_ClicksNext_CountsStage()
    {
        var (m, _) = New();
        var a = m.Step(new GameSnapshot { Attached = true, EndScreenVisible = true, NextButton = Btn }, 0);
        var c = Assert.IsType<ClickAction>(Assert.Single(Real(a)));
        Assert.Equal(Btn.CenterX, c.X);
        Assert.Equal(1, m.StagesCleared);
    }

    [Fact]
    public void Defeat_StoryCompleteText_IsNotVictory()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EndScreenVisible = true, ResultText = "DEFEAT", RetryButton = Btn,
            Hud = HudText.Parse("Story complete"),
        };
        Assert.Equal(GameScreen.Defeat, snap.Screen);
        Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal(0, m.StagesCleared);
        Assert.Equal(1, m.Retries);
    }

    [Fact]
    public void Defeat_RetriesExhausted_Stops()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { MaxRetriesPerStage = 1 });
        var snap = new GameSnapshot { Attached = true, ResultText = "DEFEAT", RetryButton = Btn };
        m.Step(snap, 0);
        var a = m.Step(snap, 5000);
        Assert.IsType<StopAction>(Assert.Single(Real(a)));
    }
}

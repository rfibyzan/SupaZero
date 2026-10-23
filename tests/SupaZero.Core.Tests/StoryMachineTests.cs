using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public class StoryMachineTests
{
    private static readonly GuiRect Btn = new(100, 100, 40, 20);

    private static (StoryMachine M, AppSettings S) New(Action<AppSettings>? cfg = null)
    {
        var s = new AppSettings();
        cfg?.Invoke(s);
        return (new StoryMachine(() => s), s);
    }

    [Fact]
    public void NotAttached_NoActions()
    {
        var (m, _) = New();
        Assert.Empty(m.Step(new GameSnapshot(), 0));
    }

    [Fact]
    public void Vote_ClicksYes_FallbackCoordinate()
    {
        var (m, _) = New();
        var a = m.Step(new GameSnapshot { Attached = true, StartVoteVisible = true }, 0);
        var c = Assert.IsType<ClickAction>(Assert.Single(a));
        Assert.Equal(AnimeZeroPaths.StartVoteYesX, c.X);
        Assert.Equal(MacroPhase.Voting, m.Phase);
        Assert.Empty(m.Step(new GameSnapshot { Attached = true, StartVoteVisible = true }, 500)); // cooldown
    }

    [Fact]
    public void Fight_AttacksAndRotatesSkills()
    {
        var (m, _) = New();
        var snap = new GameSnapshot { Attached = true, EnemyCount = 4, Hud = HudText.Parse("Easy · Area 1/5 · 4 left") };
        var a1 = m.Step(snap, 10_000);
        Assert.Contains(a1, x => x is AttackAction);
        var k1 = Assert.Single(a1.OfType<KeyAction>());
        var a2 = m.Step(snap, 11_300);
        var k2 = Assert.Single(a2.OfType<KeyAction>());
        Assert.NotEqual(k1.VirtualKey, k2.VirtualKey);
        Assert.Equal(MacroPhase.Fighting, m.Phase);
    }

    [Fact]
    public void AreaCleared_GoesNavigating()
    {
        var (m, _) = New();
        var a = m.Step(new GameSnapshot { Attached = true, EnemyCount = 0, Hud = HudText.Parse("Easy · Area 2/5 · 0 left") }, 0);
        Assert.IsType<NoteAction>(Assert.Single(a));
        Assert.Equal(MacroPhase.Navigating, m.Phase);
    }

    [Fact]
    public void Victory_ClicksNext_CountsStage()
    {
        var (m, _) = New();
        var a = m.Step(new GameSnapshot { Attached = true, EndScreenVisible = true, NextButton = Btn }, 0);
        var c = Assert.IsType<ClickAction>(Assert.Single(a));
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
        Assert.IsType<ClickAction>(Assert.Single(m.Step(snap, 0)));
        Assert.Equal(0, m.StagesCleared);
        Assert.Equal(1, m.Retries);
    }

    [Fact]
    public void Defeat_RetriesExhausted_Stops()
    {
        var (m, _) = New(s => s.Story = s.Story with { MaxRetriesPerStage = 1 });
        var snap = new GameSnapshot { Attached = true, ResultText = "DEFEAT", RetryButton = Btn };
        m.Step(snap, 0);
        var a = m.Step(snap, 5000);
        Assert.IsType<StopAction>(Assert.Single(a));
    }

    [Fact]
    public void Victory_Upgrade_SelectSlotThenBuyThenNext()
    {
        var (m, _) = New(s => s.Upgrade = s.Upgrade with { Enabled = true, MaxBuysPerStage = 1 });
        var slot = new SkillSlot("ATK", 0, true, new GuiRect(10, 10, 20, 20));
        var baseSnap = new GameSnapshot { Attached = true, EndScreenVisible = true, NextButton = Btn, SkillTreeOpen = true, Tokens = 3, Slots = [slot] };

        var a1 = m.Step(baseSnap, 0);
        Assert.Equal("Slot ATK #1", Assert.IsType<ClickAction>(Assert.Single(a1)).Reason);

        var a2 = m.Step(baseSnap with { BuyButton = new GuiRect(300, 300, 50, 20) }, 1000);
        Assert.Equal("BuyButton", Assert.IsType<ClickAction>(Assert.Single(a2)).Reason);

        var a3 = m.Step(baseSnap with { Tokens = 2, Slots = [] }, 2000);
        Assert.Equal("Next stage", Assert.IsType<ClickAction>(Assert.Single(a3)).Reason);
    }

    [Fact]
    public void Victory_UpgradeEnabled_TreeClosed_SkipsOnceThenNext()
    {
        var (m, _) = New(s => s.Upgrade = s.Upgrade with { Enabled = true });
        var snap = new GameSnapshot { Attached = true, EndScreenVisible = true, NextButton = Btn };
        Assert.IsType<NoteAction>(Assert.Single(m.Step(snap, 0)));
        Assert.IsType<ClickAction>(Assert.Single(m.Step(snap, 100)));
    }

    [Fact]
    public void Lobby_WrongAct_Notes_RightAct_ClicksDifficultyAndStart()
    {
        var (m, _) = New(s => s.Story = s.Story with { Act = 2, Difficulty = Difficulty.Hard });
        var wrong = new GameSnapshot { Attached = true, LobbyMenuOpen = true, LobbyActTag = "Act1", LobbyStart = Btn };
        Assert.IsType<NoteAction>(Assert.Single(m.Step(wrong, 0)));
        var right = wrong with { LobbyActTag = "Act2" };
        var a = m.Step(right, 100);
        Assert.Equal(2, a.Count);
        var diff = Assert.IsType<ClickAction>(a[0]);
        Assert.Equal(AnimeZeroPaths.DifficultyCenters[2].X, diff.X);
    }
}

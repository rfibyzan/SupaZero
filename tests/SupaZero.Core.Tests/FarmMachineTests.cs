using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public class FarmMachineTests
{
    private static readonly GuiRect Btn = new(100, 100, 40, 20);

    /// <summary>Buang MoveAction kosong (lepas tombol) yang ditambahkan tiap tick.</summary>
    private static List<MacroAction> Real(IReadOnlyList<MacroAction> a) => a.Where(x => x is not MoveAction { Keys.Count: 0 }).ToList();

    private static (FarmMachine M, AppSettings S) New(Action<AppSettings>? cfg = null)
    {
        var s = new AppSettings();
        cfg?.Invoke(s);
        return (new FarmMachine(() => s), s);
    }

    [Fact]
    public void NotAttached_NoActions()
    {
        var (m, _) = New();
        Assert.Empty(Real(m.Step(new GameSnapshot(), 0)));
    }

    [Fact]
    public void Vote_ClicksYes_FallbackCoordinate()
    {
        var (m, _) = New();
        var a = m.Step(new GameSnapshot { Attached = true, StartVoteVisible = true }, 0);
        var c = Assert.IsType<ClickAction>(Assert.Single(Real(a)));
        Assert.Equal(AnimeZeroPaths.StartVoteYesX, c.X);
        Assert.Equal(MacroPhase.Voting, m.Phase);
        Assert.Empty(Real(m.Step(new GameSnapshot { Attached = true, StartVoteVisible = true }, 500))); // cooldown
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
    public void AreaCleared_WalksTowardNextArea()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 0, Hud = HudText.Parse("Easy · Area 2/5 · 0 left"),
            PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), NextAreaPos = new Vec3(0, 0, -50),
        };
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal([VirtualKeys.W], move.Keys);
        Assert.Equal(MacroPhase.Navigating, m.Phase);
        // Tersangkut 2 dtk → lompat + serang rintangan
        var stuck = m.Step(snap, 2000);
        Assert.Contains(stuck, x => x is KeyAction { VirtualKey: VirtualKeys.Space });
        Assert.Contains(stuck, x => x is AttackAction);
    }

    [Fact]
    public void Lobby_FarCircle_UsesAreasTeleport()
    {
        var (m, _) = New();
        var hub = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1),
            StartCircles = [new Vec3(300, 0, 0)], AreasButton = new GuiRect(40, 540, 80, 80),
        };
        Assert.Equal("Tombol Areas", Real(m.Step(hub, 0)).OfType<ClickAction>().Single().Reason);
        var panel = hub with { TeleportCards = [new LobbyEntry("Story", false, false, new GuiRect(50, 80, 390, 170))] };
        Assert.Equal("Teleport Story", Real(m.Step(panel, 5000)).OfType<ClickAction>().Single().Reason);
    }

    [Fact]
    public void Lobby_NearDetectedCircle_Walks()
    {
        var (m, _) = New();
        var snap = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)] };
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal([VirtualKeys.D], move.Keys); // look -Z → kanan = +X
    }

    [Fact]
    public void Lobby_LockedMap_Stops()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Map = "HxH" });
        var snap = new GameSnapshot
        {
            Attached = true, LobbyMenuOpen = true, LobbyMapTag = "Sakamoto",
            LobbyMaps = [new LobbyEntry("Sakamoto", false, true, Btn), new LobbyEntry("HxH", true, false, Btn)],
        };
        Assert.Contains(m.Step(snap, 0), x => x is StopAction);
    }

    [Fact]
    public void Victory_NonStoryMode_Retries()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Mode = FarmMode.Infinite });
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

    [Fact]
    public void Victory_Upgrade_SelectSlotThenBuyThenNext()
    {
        var (m, _) = New(s => s.Upgrade = s.Upgrade with { Enabled = true, MaxBuysPerStage = 1 });
        var slot = new SkillSlot("ATK", 0, true, new GuiRect(10, 10, 20, 20));
        var baseSnap = new GameSnapshot { Attached = true, EndScreenVisible = true, NextButton = Btn, SkillTreeOpen = true, Tokens = 3, Slots = [slot] };

        var a1 = m.Step(baseSnap, 0);
        Assert.Equal("Slot ATK #1", Assert.IsType<ClickAction>(Assert.Single(Real(a1))).Reason);

        var a2 = m.Step(baseSnap with { BuyButton = new GuiRect(300, 300, 50, 20) }, 1000);
        Assert.Equal("BuyButton", Assert.IsType<ClickAction>(Assert.Single(Real(a2))).Reason);

        var a3 = m.Step(baseSnap with { Tokens = 2, Slots = [] }, 2000);
        Assert.Equal("Next stage", Assert.IsType<ClickAction>(Assert.Single(Real(a3))).Reason);
    }

    [Fact]
    public void Victory_UpgradeEnabled_TreeClosed_SkipsOnceThenNext()
    {
        var (m, _) = New(s => s.Upgrade = s.Upgrade with { Enabled = true });
        var snap = new GameSnapshot { Attached = true, EndScreenVisible = true, NextButton = Btn };
        Assert.IsType<NoteAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(snap, 100))));
    }

    [Fact]
    public void Lobby_WrongAct_Notes_RightAct_ClicksDifficultyAndStart()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Act = 2, Difficulty = Difficulty.Hard });
        var wrong = new GameSnapshot { Attached = true, LobbyMenuOpen = true, LobbyActTag = "Act1", LobbyStart = Btn };
        Assert.IsType<NoteAction>(Assert.Single(Real(m.Step(wrong, 0))));
        var right = wrong with { LobbyActTag = "Act2" };
        var a = Real(m.Step(right, 100));
        Assert.Equal(2, a.Count);
        var diff = Assert.IsType<ClickAction>(a[0]);
        Assert.Equal(AnimeZeroPaths.DifficultyCenters[2].X, diff.X);
    }
}

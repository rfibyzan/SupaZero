using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    [Fact]
    public void LobbyUpgrade_Units_Node_Buy_Exit()
    {
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true, MaxBuysPerStage = 1 }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        var units = new GuiRect(10, 300, 89, 106);
        var lobby = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), UnitsButton = units, ClientW = 1920, ClientH = 1080 };
        Assert.Equal("Units button (LobbyHUD)", Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(lobby, 0)))).Reason);

        // Snapshot 25 Oct: DEF node 3 (index 2) price 5, CanBuy; tokens 67.
        var node = new SkillSlot("DEF", 2, true, new GuiRect(1316, 519, 106, 106), 5);
        var tree = lobby with { UnitsButton = null, SkillTreeOpen = true, Tokens = 67, Slots = [node], SkillTreeExit = new GuiRect(1696, 115, 48, 48), SkillUnitSlot = 3, SkillUnitName = "Shadow Lord" };
        var first = m.Step(tree, 1000);
        Assert.Contains(first, a => a is NoteAction n && n.Reason.Contains("Shadow Lord (slot 3)"));
        Assert.Equal("Node DEF #3", Assert.IsType<ClickAction>(Assert.Single(Real(first), a => a is ClickAction)).Reason);

        var sel = tree with { Slots = [node with { Selected = true }], BuyButton = new GuiRect(1271, 767, 196, 38) };
        Assert.Equal("Buy DEF #3", Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(sel, 2200)))).Reason);

        var after = tree with { Tokens = 62, Slots = [] };
        Assert.Contains(m.Step(after, 2600), a => a is NoteAction n && n.Reason.Contains("67 → 62"));
        Assert.Contains(m.Step(after, 2700), a => a is ClickAction { Reason: "Close SkillTree (X)" });
        // Closed → continue walking (do not click Units again).
        Assert.DoesNotContain(m.Step(lobby with { UnitsButton = units }, 5000), a => a is ClickAction { Reason: "Units button (LobbyHUD)" });
    }

    private static GameSnapshot UnitTree(int sel, string? name) => new()
    {
        Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080, SkillTreeOpen = true, Tokens = 67,
        SkillUnitSlot = sel, SkillUnitName = name, SkillTreeExit = new GuiRect(1696, 115, 48, 48),
        Slots = [new SkillSlot("ATK", 1, true, new GuiRect(740, 380, 100, 100), 1)],
        SkillUnitView = new GuiRect(171, 769, 446, 177),
        // Numbers from the Shadow_Lord_Skill_Tree snapshot (25 Oct): Slot1..3 visible, Slot4..5 to the right (need scrolling), Slot6 empty.
        SkillUnits = [new("Slot1", false, sel == 1, new GuiRect(131, 799, 132, 132)), new("Slot2", false, sel == 2, new GuiRect(278, 799, 132, 132)),
                      new("Slot3", false, sel == 3, new GuiRect(424, 799, 132, 132)), new("Slot4", false, sel == 4, new GuiRect(571, 799, 132, 132)),
                      new("Slot5", false, sel == 5, new GuiRect(718, 799, 132, 132)), new("Slot6", true, false, new GuiRect(864, 799, 132, 132))],
    };

    [Fact]
    public void LobbyUpgrade_TargetSlot_ClicksSlotThenNode()
    {
        // v0.3.3: target unit = Slot 3 (Trait/Power Spin order), no name.
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true, TargetSlot = 3 }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        var a = m.Step(UnitTree(1, null), 0);
        Assert.Contains(a, x => x is ClickAction { Reason: "Unit Slot3" });
        Assert.DoesNotContain(a, x => x is ClickAction { Reason: var r } && r.StartsWith("Node"));
        Assert.Contains(m.Step(UnitTree(3, null), 1000), x => x is ClickAction { Reason: "Node ATK #2" });
    }

    [Fact]
    public void LobbyUpgrade_TargetSlotOutOfView_ScrollsList()
    {
        // Shadow_Lord_Skill_Tree snapshot: Slot5 (x=718) outside the visible list (171..617) → scroll first, do not click.
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true, TargetSlot = 5 }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        var a = m.Step(UnitTree(1, null), 0);
        Assert.DoesNotContain(a, x => x is ClickAction { Reason: "Unit Slot5" });
        Assert.Contains(a, x => x is ScrollAction);
    }

    [Fact]
    public void LobbyUpgrade_TargetSlotEmpty_ClosesWithoutBuying()
    {
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true, TargetSlot = 6 }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        var a = m.Step(UnitTree(1, null), 0);
        Assert.Contains(a, x => x is NoteAction n && n.Reason.Contains("Slot 6 empty"));
        Assert.DoesNotContain(a, x => x is ClickAction { Reason: var r } && (r.StartsWith("Node") || r.StartsWith("Unit")));
    }

    [Fact]
    public void LobbyUpgrade_TargetSlotAlreadySelected_ClicksNode()
    {
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true, TargetSlot = 2 }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        Assert.Contains(m.Step(UnitTree(2, null), 0), x => x is ClickAction { Reason: "Node ATK #2" });
    }

    [Fact]
    public void LobbyUpgrade_DryRun_NeverClicksBuy_ThenStops()
    {
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true, DryRun = true }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        var tree = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080, SkillTreeOpen = true, Tokens = 67,
            SkillUnitSlot = 3, SkillUnitName = "Shadow Lord", Slots = [new SkillSlot("ATK", 1, true, new GuiRect(10, 10, 100, 100), 1) { Selected = true }],
            BuyButton = new GuiRect(900, 800, 120, 40), SkillTreeExit = new GuiRect(1696, 115, 48, 48) };
        var a = m.Step(tree, 0);
        Assert.DoesNotContain(a, x => x is ClickAction { Reason: var r } && r.StartsWith("Buy"));
        Assert.Contains(a, x => x is NoteAction n && n.Reason.Contains("DRY RUN"));
        var b = m.Step(tree, 2000);
        Assert.Contains(b, x => x is ClickAction { Reason: "Close SkillTree (X)" });
        var c = m.Step(tree with { SkillTreeOpen = false }, 4000);
        Assert.Contains(c, x => x is StopAction);
    }

    [Fact]
    public void LobbyUpgrade_OnlyAtkOn_SkipsCritEvenIfCheaper()
    {
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true, EnabledColumns = ["ATK"], Priority = ["CRIT", "ATK", "SPD", "DEF"] }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        var tree = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080, SkillTreeOpen = true, Tokens = 10,
            Slots = [new SkillSlot("CRIT", 1, true, new GuiRect(300, 10, 100, 100), 1), new SkillSlot("ATK", 2, true, new GuiRect(10, 210, 100, 100), 5)], SkillTreeExit = new GuiRect(1696, 115, 48, 48) };
        Assert.Contains(m.Step(tree, 0), x => x is ClickAction { Reason: "Node ATK #3" });
    }

    [Fact]
    public void LobbyUpgrade_AllColumnsOff_BuysNothing()
    {
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true, EnabledColumns = [] }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        var tree = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080, SkillTreeOpen = true, Tokens = 10,
            Slots = [new SkillSlot("ATK", 1, true, new GuiRect(10, 10, 100, 100), 1)], SkillTreeExit = new GuiRect(1696, 115, 48, 48) };
        var a = m.Step(tree, 0);
        Assert.DoesNotContain(a, x => x is ClickAction { Reason: var r } && r.StartsWith("Node"));
        Assert.Contains(a, x => x is NoteAction n && n.Reason.Contains("no columns"));
    }

    [Fact]
    public void LobbyUpgrade_PriceAboveTokens_ClosesWithoutBuying()
    {
        var (m, _) = New(s => { s.Upgrade = s.Upgrade with { Enabled = true }; s.Farm = s.Farm with { CheckPowersInLobby = false }; });
        var tree = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080, SkillTreeOpen = true, Tokens = 4,
            Slots = [new SkillSlot("ATK", 2, true, new GuiRect(10, 10, 100, 100), 5)], SkillTreeExit = new GuiRect(1696, 115, 48, 48) };
        var a = m.Step(tree, 0);
        Assert.DoesNotContain(a, x => x is ClickAction { Reason: var r } && r.StartsWith("Node"));
        Assert.Contains(m.Step(tree, 100), x => x is ClickAction { Reason: "Close SkillTree (X)" });
    }
}

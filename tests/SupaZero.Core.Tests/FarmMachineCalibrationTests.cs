using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    // ---------- v0.2.82 action calibration ----------
    [Fact]
    public void Calibration_ReadsModesMapsActs_ThenLeaves_NeverStarts()
    {
        var (m, _) = New();
        CalibrationScan? scan = null;
        m.CalibrationFinished = x => scan = x;
        m.StartCalibration();
        var leave = new GuiRect(1600, 900, 80, 60);
        var menu = new GameSnapshot
        {
            Attached = true, LobbyMenuOpen = true, ClientW = 1920, ClientH = 1080, LobbyMapTag = "Hxh", LobbyStart = Btn, LobbyLeave = leave,
            LobbyModes = [new LobbyEntry("Story", false, true, new GuiRect(100, 100, 100, 40))],
            LobbyMaps = [new LobbyEntry("Hxh", false, true, new GuiRect(300, 300, 200, 60), "Story"), new LobbyEntry("Tidal High Rise", true, false, new GuiRect(300, 400, 200, 60), "Story")],
            LobbyActs = Enumerable.Range(1, 3).Select(i => new LobbyEntry($"Act {i}", i == 3, i == 1, new GuiRect(600, 100 + i * 50, 100, 40))).ToList(),
        };
        var all = new List<MacroAction>();
        for (long t = 0; t <= 8000; t += 300) all.AddRange(m.Step(menu, t));
        Assert.DoesNotContain(all, a => a is ClickAction c && (c.Reason.StartsWith("Start") || c.Reason.StartsWith("Difficulty")));
        Assert.Contains(all, a => a is ClickAction c && c.Reason.StartsWith("Leave"));
        var lobby = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)], ClientW = 1920, ClientH = 1080 };
        Assert.Contains(m.Step(lobby, 9000), a => a is StopAction);
        Assert.NotNull(scan);
        Assert.Equal([1, 2, 3], scan!.Acts["Hxh"]);
        Assert.Equal(2, scan.Maps.Count);
        Assert.False(m.Calibrating);
    }

    [Fact]
    public void Calibration_ReadsTargetableDropsPerAct_AtNightmare()
    {
        // v0.3.4 (Byzan 25 Oct 08:38): drops differ per act (Hxh Act 1 = A,B; Act 2 = C,D) → calibration clicks Nightmare + each act, reads TargetPill.
        var (m, _) = New();
        CalibrationScan? scan = null;
        m.CalibrationFinished = x => scan = x;
        m.StartCalibration();
        string diff = "Easy"; int act = 1;
        var diffs = new[] { "Easy", "Medium", "Hard", "Nightmare" }.Select((d, i) => new LobbyEntry(d, false, false, new GuiRect(100 + i * 120, 600, 100, 100))).ToList();
        GameSnapshot Snap() => new()
        {
            Attached = true, LobbyMenuOpen = true, ClientW = 1920, ClientH = 1080, LobbyMapTag = "Hxh", LobbyStart = Btn, LobbyLeave = new GuiRect(1600, 900, 80, 60),
            LobbyModes = [new LobbyEntry("Story", false, true, new GuiRect(100, 100, 100, 40))],
            LobbyMaps = [new LobbyEntry("Hxh", false, true, new GuiRect(300, 300, 200, 60), "Story")],
            LobbyActs = [new LobbyEntry("Act 1", false, act == 1, new GuiRect(600, 150, 100, 40)), new LobbyEntry("Act 2", false, act == 2, new GuiRect(600, 200, 100, 40))],
            LobbyActTag = $"Hxh [Act {act}]", LobbyDifficultyTag = $"[{diff}]", LobbyDifficulties = diffs,
            LobbyRewards = act == 1
                ? [new RewardCard("Gems", false, false, null, null), new RewardCard("Material A", diff is "Hard" or "Nightmare", false, new GuiRect(700, 800, 80, 80), "14%"), new RewardCard("Material B", diff is "Hard" or "Nightmare", false, new GuiRect(800, 800, 80, 80), "19%")]
                : [new RewardCard("Material C", true, false, new GuiRect(700, 800, 80, 80), "14%"), new RewardCard("Material D", true, false, new GuiRect(800, 800, 80, 80), "28%")],
        };
        var all = new List<MacroAction>();
        for (long t = 0; t <= 30000 && scan is null; t += 300)
        {
            var acts = m.Step(Snap(), t);
            all.AddRange(acts);
            foreach (var c in acts.OfType<ClickAction>())
            {
                if (c.Reason.StartsWith("Difficulty Nightmare")) diff = "Nightmare";
                if (c.Reason.StartsWith("Act 2")) act = 2;
                if (c.Reason.StartsWith("Act 1")) act = 1;
            }
            if (all.Any(a => a is ClickAction c && c.Reason.StartsWith("Leave")))
                m.Step(new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)], ClientW = 1920, ClientH = 1080 }, t + 100);
        }
        Assert.DoesNotContain(all, a => a is ClickAction c && c.Reason.StartsWith("Start"));
        Assert.NotNull(scan);
        Assert.Equal(["Material A", "Material B"], scan!.Drops[FarmMachine.DropKey("Hxh", 1)]);
        Assert.Equal(["Material C", "Material D"], scan.Drops[FarmMachine.DropKey("Hxh", 2)]);
    }

    [Fact]
    public void Calibration_TrialsLast_ReadsFloorRewards_ThenLeavesTrials()
    {
        // v0.3.7 (Byzan 25 Oct 09:03): Trials card → Trials panel (Floor Rewards) → drops → Leave Trials closes the room menu.
        var (m, _) = New();
        CalibrationScan? scan = null;
        m.CalibrationFinished = x => scan = x;
        m.StartCalibration();
        var menu = new GameSnapshot
        {
            Attached = true, LobbyMenuOpen = true, ClientW = 1920, ClientH = 1080, LobbyMapTag = "Hxh", LobbyLeave = new GuiRect(1600, 900, 80, 60),
            LobbyMaps = [new LobbyEntry("Hxh", false, true, new GuiRect(300, 300, 200, 60), "Story"), new LobbyEntry("Trials", false, false, new GuiRect(300, 400, 200, 60), "Trials")],
            LobbyActs = [new LobbyEntry("Act 1", false, true, new GuiRect(600, 150, 100, 40))],
        };
        var all = new List<MacroAction>();
        long t = 0;
        for (; t <= 8000 && !all.Any(a => a is ClickAction c && c.Reason.StartsWith("Map Trials")); t += 300) all.AddRange(m.Step(menu, t));
        Assert.Contains(all, a => a is ClickAction c && c.Reason.StartsWith("Map Trials"));
        Assert.DoesNotContain(all, a => a is ClickAction c && c.Reason.StartsWith("Leave"));
        var trials = new TrialsInfo(new GuiRect(40, 190, 600, 55), true, 2, 1, null, null, new GuiRect(1343, 911, 260, 60),
            [new RewardCard("Gems", false, false, null, null), new RewardCard("Meat", true, false, new GuiRect(1610, 770, 90, 90), "36%")], new GuiRect(1150, 740, 720, 140))
        { Leave = new GuiRect(1617, 911, 260, 60) };
        var tri = new GameSnapshot { Attached = true, ClientW = 1920, ClientH = 1080, Trials = trials };
        for (long e = t + 2000; t <= e; t += 200) all.AddRange(m.Step(tri, t));
        Assert.Contains(all, a => a is ClickAction c && c.Reason.StartsWith("Leave Trials"));
        Assert.DoesNotContain(all, a => a is ClickAction c && c.Reason.Contains("Start"));
        var lobby = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)], ClientW = 1920, ClientH = 1080 };
        Assert.Contains(m.Step(lobby, t + 300), a => a is StopAction);
        Assert.Equal(["Meat"], scan!.Drops[FarmMachine.DropKey("Trials", 2)]);
    }

    [Fact]
    public void Calibration_MapsInListOrder_SinglePassAcrossModes()
    {
        var (m, _) = New();
        m.StartCalibration();
        string tag = "Hxh";
        GameSnapshot Snap() => new()
        {
            Attached = true, LobbyMenuOpen = true, ClientW = 1920, ClientH = 1080, LobbyMapTag = tag, LobbyLeave = new GuiRect(1600, 900, 80, 60),
            LobbyModes = [new LobbyEntry("Story", false, true, new GuiRect(100, 100, 100, 40)), new LobbyEntry("Raid", false, false, new GuiRect(100, 150, 100, 40))],
            // read order differs from display order (Y)
            LobbyMaps = [new LobbyEntry("Spirit's Bathtub", false, false, new GuiRect(300, 500, 200, 60), "Raid"), new LobbyEntry("Hxh", false, tag == "Hxh", new GuiRect(300, 100, 200, 60), "Story"),
                new LobbyEntry("Evil Reflection", false, false, new GuiRect(300, 300, 200, 60), "Raid")],
            LobbyActs = [new LobbyEntry("Act 1", false, true, new GuiRect(600, 150, 100, 40))],
        };
        var order = new List<string>(); var all = new List<MacroAction>();
        for (long t = 0; t <= 20000 && !all.Any(x => x is ClickAction c && c.Reason.StartsWith("Leave")); t += 200)
        {
            var a = m.Step(Snap(), t); all.AddRange(a);
            foreach (var c in a.OfType<ClickAction>().Where(c => c.Reason.StartsWith("Map "))) { tag = c.Reason[4..]; order.Add(tag); }
        }
        Assert.Equal(["Evil Reflection", "Spirit's Bathtub"], order);
        Assert.DoesNotContain(all, a => a is ClickAction c && c.Reason.StartsWith("Mode Raid"));
    }

    [Fact]
    public void Calibration_StatusPrefixDoesNotStack()
    {
        var (m, _) = New();
        m.StartCalibration();
        var lobby = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)], ClientW = 1920, ClientH = 1080 };
        for (long t = 0; t <= 9000; t += 300) m.Step(lobby, t);
        Assert.DoesNotContain("Calibration: Calibration", m.Status);
    }

    [Fact]
    public void Calibration_AfterTraits_ClicksStoryCard_EvenIfFarmModeDiffers()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Mode = "Raid", CheckPowersInLobby = false });
        m.StartCalibration();
        var far = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(-85.8, 5.4, -90.2), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080,
            StartRooms = [new StartRoom("2", new Vec3(-83.7, 6.1, 264.4), false, null, null)],
            TeleportCards = [new LobbyEntry("Story", false, false, new GuiRect(100, 300, 200, 100)), new LobbyEntry("Raid", false, false, new GuiRect(400, 300, 200, 100))],
            AreasView = new GuiRect(50, 50, 900, 600),
        };
        var acts = new List<MacroAction>();
        for (long t = 0; t <= 24_000; t += 500) acts.AddRange(m.Step(far, t)); // trait skipped (no Traits card), Spin button waited 4 s (v0.3.39), then to the room
        Assert.Contains(acts, a => a is ClickAction c && c.Reason == "Teleport Story");
        Assert.DoesNotContain(acts, a => a is ClickAction c && c.Reason == "Teleport Raid");
    }

    [Fact]
    public void Calibration_Traits_CyclesAllUnitsWithNextButton()
    {
        // RIE Trait_Reroll 25 Oct 08:54: ">" button (582,638 42x42) → "Shadow Lord 3/5" becomes "Mochi Commander 4/5".
        Assert.Equal((3, 5), FarmMachine.TraitCounter("Shadow Lord 3/5"));
        var (m, _) = New();
        m.StartCalibration();
        var spot = new Vec3(-87.7, 0.4, -97);
        var baseS = new GameSnapshot { Attached = true, PlayerPos = new Vec3(-87.7, 3, -97), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080, TraitSpot = spot, TraitExit = new Vec3(-85.8, 5.4, -90.2) };
        string[] names = ["Divine Emperor", "Cursed King", "Shadow Lord", "Mochi Commander", "Twisted Agony"];
        int idx = 2;
        GameSnapshot Open() => baseS with { TraitMenuOpen = true, TraitHeader = $"{names[idx]} {idx + 1}/5", TraitNext = new GuiRect(582, 638, 42, 42) };
        var clicks = 0; bool exited = false;
        for (long t = 0; t <= 40000 && !exited; t += 250)
        {
            var a = m.Step(Open(), t);
            foreach (var c in a.OfType<ClickAction>().Where(c => c.Reason.StartsWith("Trait >"))) { clicks++; idx = (idx + 1) % 5; }
            exited = a.Any(x => x is MoveAction mv && mv.Keys.Count > 0 && mv.Reason.Contains("outside the Enchants zone"));
        }
        Assert.Equal(4, clicks);   // 3/5 → 4 → 5 → 1 → 2: five units read, no extra clicks
        Assert.True(exited);
    }

    [Fact]
    public void Calibration_Traits_NextClickWithoutChange_Stops()
    {
        var (m, _) = New();
        m.StartCalibration();
        var baseS = new GameSnapshot { Attached = true, PlayerPos = new Vec3(-87.7, 3, -97), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080, TraitSpot = new Vec3(-87.7, 0.4, -97), TraitExit = new Vec3(-85.8, 5.4, -90.2),
            TraitMenuOpen = true, TraitHeader = "Shadow Lord 3/5", TraitNext = new GuiRect(582, 638, 42, 42) };
        var all = new List<MacroAction>();
        for (long t = 0; t <= 12000; t += 250) all.AddRange(m.Step(baseS, t));
        Assert.Single(all, a => a is ClickAction c && c.Reason.StartsWith("Trait >"));
        Assert.Contains(all, a => a is NoteAction n && n.Reason.Contains("did not change the unit"));
    }

    [Fact]
    public void Calibration_Traits_WalksToGuideSpot_ThenExitsZone()
    {
        var (m, _) = New();
        m.StartCalibration();
        var spawn = new Vec3(-85.8, 5.4, -90.2);
        var baseS = new GameSnapshot { Attached = true, PlayerPos = spawn, CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)], ClientW = 1920, ClientH = 1080 };
        var panel = baseS with { TeleportCards = [new LobbyEntry("Traits", false, false, new GuiRect(100, 300, 200, 100))], AreasView = new GuiRect(50, 50, 600, 600) };
        Assert.Contains(m.Step(panel, 0), a => a is ClickAction c && c.Reason == "Teleport Traits");
        // No GuideSpot point → no walking.
        var noSpot = new List<MacroAction>();
        for (long t = 500; t <= 2500; t += 500) noSpot.AddRange(m.Step(baseS, t));
        Assert.DoesNotContain(noSpot, a => a is MoveAction mv && mv.Keys.Count > 0 && mv.Reason.Contains("Enchants"));

        var (m2, _) = New();
        m2.StartCalibration();
        var withSpot = baseS with { TraitSpot = new Vec3(-87.7, 0.4, -97), TraitExit = spawn };
        m2.Step(withSpot with { TeleportCards = panel.TeleportCards, AreasView = panel.AreasView }, 0);
        var acts = m2.Step(withSpot, 2500);
        Assert.Contains(acts, a => a is MoveAction mv && mv.Keys.Count > 0 && mv.Reason.Contains("Enchants spot"));
        var open = withSpot with { PlayerPos = new Vec3(-87.7, 3, -97), TraitMenuOpen = true };
        m2.Step(open, 3000);
        m2.Step(open, 6100);
        Assert.Contains(m2.Step(open, 6400), a => a is MoveAction mv && mv.Keys.Count > 0 && mv.Reason.Contains("outside the Enchants zone"));
    }
}

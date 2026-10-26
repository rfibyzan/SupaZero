using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    [Fact]
    public void Vote_ClicksYes_OnlyWhenButtonRead()
    {
        var (m, _) = New();
        // v0.2.79: Yes button not read → do not click fallback coordinates.
        Assert.DoesNotContain(m.Step(new GameSnapshot { Attached = true, StartVoteVisible = true }, 0), x => x is ClickAction);
        var snap = new GameSnapshot { Attached = true, StartVoteVisible = true, StartVoteYes = new GuiRect(900, 600, 100, 40) };
        var c = Assert.Single(m.Step(snap, 100).OfType<ClickAction>());
        Assert.Equal(950, c.X);
        Assert.Equal(MacroPhase.Voting, m.Phase);
        Assert.Empty(m.Step(snap, 600).OfType<ClickAction>()); // cooldown
    }

    [Fact]
    public void Lobby_FarCircle_UsesAreasTeleport()
    {
        var (m, _) = New();
        var hub = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080,
            StartCircles = [new Vec3(300, 0, 0)], AreasButton = new GuiRect(40, 540, 80, 80),
        };
        Assert.Equal("Areas button", Real(m.Step(hub, 0)).OfType<ClickAction>().Single().Reason);
        var panel = hub with { TeleportCards = [new LobbyEntry("Story", false, false, new GuiRect(50, 80, 390, 170))] };
        Assert.Equal("Teleport Story", Real(m.Step(panel, 5000)).OfType<ClickAction>().Single().Reason);
        // After teleport, circle at 80 studs (teleport panel still shown) → walk, do not re-teleport.
        var after = panel with { PlayerPos = new Vec3(220, 0, 0) };
        var walk = Real(m.Step(after, 7000));
        Assert.DoesNotContain(walk, x => x is ClickAction);
        Assert.Contains(walk.OfType<MoveAction>(), mv => mv.Keys.Count > 0);
    }

    [Fact]
    public void Lobby_AfterTeleport_WalksToFarReadCircle_NoReteleport()
    {
        // Byzan 25 Oct 10:18: after the Story teleport it idled on "Waiting after teleport" then re-teleported; circle > 90 studs.
        var (m, _) = New();
        var hub = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080,
            StartRooms = [new StartRoom("1", new Vec3(400, 0, 0), false)], AreasButton = new GuiRect(40, 540, 80, 80),
        };
        Assert.Equal("Areas button", Real(m.Step(hub, 0)).OfType<ClickAction>().Single().Reason);
        var panel = hub with { TeleportCards = [new LobbyEntry("Story", false, false, new GuiRect(50, 80, 390, 170))] };
        Assert.Equal("Teleport Story", Real(m.Step(panel, 5000)).OfType<ClickAction>().Single().Reason);
        var after = hub with { PlayerPos = new Vec3(150, 0, 0) }; // 250 studs from the circle
        for (long t = 6000; t <= 25_000; t += 500)
        {
            var a = Real(m.Step(after with { PlayerPos = new Vec3(150 + (t - 6000) / 100.0, 0, 0) }, t));
            Assert.DoesNotContain(a, x => x is ClickAction);
            Assert.Contains(a.OfType<MoveAction>(), mv => mv.Keys.Count > 0);
        }
    }

    [Fact]
    public void Lobby_CardReadWhilePanelClosed_OpensAreasFirst_AndRetriesWhenNoTeleport()
    {
        // Byzan 25 Oct 10:20: Story card read although the Areas panel was not opened → previously clicked nothing, then "Waiting after teleport".
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080,
            AreasButton = new GuiRect(40, 540, 80, 80), // circle not read yet (far away)
            TeleportCards = [new LobbyEntry("Story", false, false, new GuiRect(50, 80, 390, 170))],
        };
        Assert.Equal("Areas button", Real(m.Step(snap, 0)).OfType<ClickAction>().Single().Reason);
        Assert.Equal("Teleport Story", Real(m.Step(snap, 1000)).OfType<ClickAction>().Single().Reason);
        // Position unchanged → no teleport. After 3 s: open Areas again instead of idling 12 s.
        var clicks = new List<string>();
        for (long t = 1500; t <= 6000; t += 500) clicks.AddRange(Real(m.Step(snap, t)).OfType<ClickAction>().Select(c => c.Reason));
        Assert.Contains("Areas button", clicks);
    }

    [Fact]
    public void Lobby_StuckAtRing_SwitchesAfterJoinTimeoutSetting()
    {
        // Circle = barrier: distance stops at ~11 studs. "Switch circle" setting 2 s → switch within ≤ 2.5 s (previously 6 s + walking to the center).
        var (m, _) = New(s => s.Farm = s.Farm with { JoinTimeoutSec = 2 });
        var snap = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(11, 0, 0), CameraLook = (0, -1),
            StartRooms = [new StartRoom("1", new Vec3(0, 0, 0), false), new StartRoom("2", new Vec3(30, 0, 0), false)],
        };
        long switchedAt = -1;
        for (long t = 0; t <= 6000 && switchedAt < 0; t += 250)
            if (m.Step(snap, t).OfType<NoteAction>().Any(n => n.Reason.Contains("Cannot join room 1"))) switchedAt = t;
        Assert.InRange(switchedAt, 1500, 2500);
    }

    [Fact]
    public void Lobby_NearRoomFailed_RetriesNearInsteadOfTeleport()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), AreasButton = new GuiRect(40, 540, 80, 80),
            StartRooms = [new StartRoom("2", new Vec3(0, 0, 0), false), new StartRoom("9", new Vec3(500, 0, 0), false)],
        };
        long t = 0;
        for (; t <= 40_000; t += 500) m.Step(snap, t); // arrive → join fails → room 2 banned
        var a = Real(m.Step(snap, t));
        Assert.DoesNotContain(a, x => x is ClickAction);
    }

    [Fact]
    public void Lobby_NearDetectedCircle_Walks()
    {
        var (m, _) = New();
        var snap = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)] };
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal([VirtualKeys.D], move.Keys); // look -Z → right = +X
    }

    [Fact]
    public void Lobby_SkipsOccupiedRoom()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1),
            StartRooms = [new StartRoom("1", new Vec3(10, 0, 0), Occupied: true, 3, 4), new StartRoom("2", new Vec3(-30, 0, 0), false)],
        };
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal([VirtualKeys.A], move.Keys); // room 1 occupied → go to room 2 (-X = left)
    }

    [Fact]
    public void Lobby_JoinTimeout_TriesNextRoom()
    {
        var (m, _) = New();
        var rooms = new[] { new StartRoom("1", new Vec3(1, 0, 0), false), new StartRoom("2", new Vec3(-30, 0, 0), false) };
        var snap = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartRooms = rooms };
        Assert.Empty(Real(m.Step(snap, 0)));      // already at room 1, waiting for the menu
        Assert.Empty(Real(m.Step(snap, 3000)));
        Assert.Contains(Real(m.Step(snap, 5500)), a => a is NoteAction n && n.Reason.Contains("room 1"));
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 6000))));
        Assert.Equal([VirtualKeys.A], move.Keys); // room 1 banned → go to room 2
    }

    [Fact]
    public void Lobby_WalkStalledNearRoom_SwitchesRoom()
    {
        // Byzan screenshot 08:24 (25 Oct): status kept showing "Walking to Create Room circle (11 studs)" although the character was on the circle & the menu did not open.
        var (m, _) = New();
        var rooms = new[] { new StartRoom("1", new Vec3(11, 0, 0), false), new StartRoom("2", new Vec3(-30, 0, 0), false) };
        var snap = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartRooms = rooms };
        Assert.Contains(Real(m.Step(snap, 0)), a => a is MoveAction { Keys.Count: > 0 });
        Assert.DoesNotContain(Real(m.Step(snap, 4000)), a => a is NoteAction);
        Assert.Contains(Real(m.Step(snap, 6500)), a => a is NoteAction n && n.Reason.Contains("room 1") && n.Reason.Contains("stalled"));
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 7000))));
        Assert.Equal([VirtualKeys.A], move.Keys); // room 1 banned → go to room 2
    }

    [Fact]
    public void Lobby_ActMissingOnMap_Stops()
    {
        // v0.2.80: requested act does not exist → stop (do not switch act on its own).
        var (m, _) = New(s => s.Farm = s.Farm with { Act = 5 });
        var snap = new GameSnapshot
        {
            Attached = true, LobbyMenuOpen = true, LobbyMapTag = "Training", LobbyActTag = "Training [Act 1]",
            LobbyActs = [new LobbyEntry("Act 1 Training", false, true, Btn)], LobbyStart = Btn, ClientW = 1920, ClientH = 1080,
        };
        var a = Real(m.Step(snap, 0));
        Assert.Contains(a, x => x is StopAction st && st.Reason.Contains("Act 5 not found"));
        Assert.DoesNotContain(a, x => x is ClickAction);
    }

    [Fact]
    public void Lobby_AllActs_PicksNextAfterProgress()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Act = 0, ActProgress = new() { ["Hxh"] = 2 } });
        var acts = Enumerable.Range(1, 5).Select(i => new LobbyEntry($"Act {i}", false, i == 1, new GuiRect(i * 100, 100, 50, 20))).ToList();
        var snap = new GameSnapshot { Attached = true, LobbyMenuOpen = true, LobbyMapTag = "Hxh", LobbyActTag = "Hxh [Act 1]", LobbyActs = acts, ClientW = 1920, ClientH = 1080 };
        Assert.Equal("Act 3", Real(m.Step(snap, 0)).OfType<ClickAction>().Single().Reason);
    }

    [Fact]
    public void ActNumber_Parses()
    {
        Assert.Equal(1, FarmMachine.ActNumber("Training [Act 1]"));
        Assert.Equal(3, FarmMachine.ActNumber("Act 3 Training"));
        Assert.Equal(0, FarmMachine.ActNumber("Training"));
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
    public void Lobby_WrongAct_Notes_RightAct_ClicksDifficultyAndStart()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Act = 2, Difficulty = Difficulty.Hard });
        var hard = new GuiRect(1209, 527, 103, 123);
        var wrong = new GameSnapshot { Attached = true, LobbyMenuOpen = true, LobbyActTag = "Act1", LobbyStart = Btn, ClientW = 1920, ClientH = 1080,
            LobbyDifficulties = [new LobbyEntry("Hard", false, false, hard)] };
        Assert.IsType<NoteAction>(Assert.Single(Real(m.Step(wrong, 0))));
        // v0.2.80: selected difficulty not read → click nothing.
        var right = wrong with { LobbyActTag = "Act2" };
        Assert.DoesNotContain(m.Step(right, 100), x => x is ClickAction);
        var easy = right with { LobbyDifficultyTag = "EASY" };
        var diff = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(easy, 200))));
        Assert.Equal(hard.CenterX, diff.X);
        Assert.Empty(Real(m.Step(easy, 400)));                        // wait for the difficulty to register
        var start = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(easy with { LobbyDifficultyTag = "HARD" }, 1500))));
        Assert.Equal(Btn.CenterX, start.X);
    }

    [Fact]
    public void Lobby_MapBelowList_ScrollsDown_ThenClicks()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Map = "Tidal High Rise" });
        var view = new GuiRect(200, 300, 400, 300);                  // visible list y 300..600
        var hidden = new LobbyEntry("Tidal High Rise", false, false, new GuiRect(220, 900, 360, 80));
        var snap = new GameSnapshot { Attached = true, LobbyMenuOpen = true, LobbyMapTag = "Hxh", LobbyMaps = [hidden], LobbyMapsView = view, ClientW = 1280, ClientH = 720 };
        var sc = Assert.IsType<ScrollAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.True(sc.Notches < 0);
        Assert.True(view.Contains(sc.X, sc.Y));
        var shown = snap with { LobbyMaps = [hidden with { Rect = new GuiRect(220, 420, 360, 80) }] };
        var click = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(shown, 1000))));
        Assert.Equal(460, click.Y);
    }

    [Fact]
    public void Lobby_DifficultyTag_ClicksUntilMatch_ThenStart()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Act = 1, Difficulty = Difficulty.Hard });
        var hard = new GuiRect(1209, 527, 103, 123);
        var snap = new GameSnapshot
        {
            Attached = true, LobbyMenuOpen = true, LobbyActTag = "[Act 1]", LobbyDifficultyTag = "EASY", LobbyStart = Btn, ClientW = 1920, ClientH = 1080,
            LobbyDifficulties = [new LobbyEntry("Hard", false, false, hard)],
        };
        var c = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal(hard.CenterX, c.X);
        var ok = snap with { LobbyDifficultyTag = "HARD" };
        var start = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(ok, 1000))));
        Assert.StartsWith("Start", start.Reason);
    }

    [Fact]
    public void Queue_ClicksSkipOnlyWhenFree()
    {
        var (m, _) = New(s => { });
        var skip = new GuiRect(639, 901, 315, 61);
        var snap = new GameSnapshot { Attached = true, QueueSkip = skip, ClientW = 1920, ClientH = 1080 };
        var c = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal("Skip Waiting", c.Reason);
        Assert.Empty(Real(m.Step(snap with { QueueSkipCosts = true }, 5000)).OfType<ClickAction>());
    }

    [Fact]
    public void Lobby_NeverClicksOutsideWindow()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Map = "Tidal High Rise" });
        var hidden = new LobbyEntry("Tidal High Rise", false, false, new GuiRect(220, 900, 360, 80));
        var snap = new GameSnapshot { Attached = true, LobbyMenuOpen = true, LobbyMapTag = "Hxh", LobbyMaps = [hidden], ClientW = 1280, ClientH = 720 };
        Assert.DoesNotContain(m.Step(snap, 0), a => a is ClickAction);
    }

    [Fact]
    public void Farm_RaidMode_TeleportsViaStoryCard()
    {
        // Byzan log 08:29:47: Raid mode → "Teleport card 'Raid' not found (read: Story, Arsenal, ...)". Create Room circles are in the Story area.
        var (m, _) = New(s => s.Farm = s.Farm with { Mode = "Raid", CheckPowersInLobby = false });
        var far = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), ClientW = 1920, ClientH = 1080,
            StartRooms = [new StartRoom("6", new Vec3(131, 0, 0), false, null, null)],
            TeleportCards = [new LobbyEntry("Story", false, false, new GuiRect(100, 300, 200, 100)), new LobbyEntry("Arsenal", false, false, new GuiRect(400, 300, 200, 100))],
            AreasView = new GuiRect(50, 50, 900, 600),
        };
        var acts = new List<MacroAction>();
        for (long t = 0; t <= 3000; t += 500) acts.AddRange(m.Step(far, t));
        Assert.Contains(acts, a => a is ClickAction c && c.Reason == "Teleport Story");
        Assert.DoesNotContain(acts, a => a is NoteAction n && n.Reason.Contains("not found"));
    }
}

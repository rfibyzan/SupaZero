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
        Assert.DoesNotContain(a1, x => x is AttackAction); // tick dengan skill → basic attack dilewati
        var k1 = Assert.Single(a1.OfType<KeyAction>().Where(k => k.VirtualKey != VirtualKeys.Space && !k.Reason.Contains("Dash")));
        var a2 = m.Step(snap, 11_300);
        var k2 = Assert.Single(a2.OfType<KeyAction>().Where(k => k.VirtualKey != VirtualKeys.Space && !k.Reason.Contains("Dash")));
        Assert.NotEqual(k1.VirtualKey, k2.VirtualKey);
        Assert.Equal(MacroPhase.Fighting, m.Phase);
    }

    [Fact]
    public void Fight_KeepsAttackRange_BacksAwayWhenTooClose()
    {
        var (m, _) = New(s => s.Navigation = s.Navigation with { AttackRange = 40 });
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 1/5 · 1 left"),
            PlayerPos = new(0, 3, 0), CameraLook = (0, -1), Enemies = [new("1", new(0, 3, -15), 100, 100)],
        };
        var mv = m.Step(snap, 10_000).OfType<MoveAction>().First(x => x.Keys.Count > 0);
        Assert.Contains(VirtualKeys.S, mv.Keys); // musuh di depan 15 stud < 40 → mundur
        var far = snap with { Enemies = [new("1", new(0, 3, -80), 100, 100)] };
        var mf = m.Step(far, 10_100).OfType<MoveAction>().First(x => x.Keys.Count > 0);
        Assert.Contains(VirtualKeys.W, mf.Keys); // 80 > 44 → kejar
    }

    [Fact]
    public void Fight_UsesHudCooldowns()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 4, Hud = HudText.Parse("Easy · Area 1/5 · 4 left"),
            SkillCooldowns = [new("1", 1.7, false), new("2", null, true), new("3", null, false)],
        };
        var k = Assert.Single(m.Step(snap, 10_000).OfType<KeyAction>().Where(k => k.VirtualKey != VirtualKeys.Space && !k.Reason.Contains("Dash")));
        Assert.Contains("(3)", k.Reason);
        Assert.Empty(m.Step(snap, 10_200).OfType<KeyAction>().Where(k => k.VirtualKey != VirtualKeys.Space && !k.Reason.Contains("Dash"))); // 1 cooldown, 2 terkunci, 3 baru dipakai
        Assert.Single(m.Step(snap, 10_700).OfType<KeyAction>().Where(k => k.VirtualKey != VirtualKeys.Space && !k.Reason.Contains("Dash"))); // HUD bilang 3 siap lagi
    }

    [Fact]
    public void Camera_ProjectsEnemyFromSnapshot()
    {
        // Snapshot 23 Okt 2026: kamera (75.508,20.141,-330.377) look (0,-0.744,-0.668) FOV 70 viewport 1920x1009.
        var cam = new CameraInfo(new(75.508, 20.141, -330.377), new(0, -0.744, -0.668), 70, 1920, 1009);
        var p = cam.WorldToScreen(new(74.8, 3.001, -362));
        Assert.NotNull(p);
        Assert.InRange(p!.Value.X, 930, 960);   // sedikit kiri tengah
        Assert.InRange(p.Value.Y, 200, 300);    // lebih jauh ke depan → bagian atas layar
        Assert.Null(cam.WorldToScreen(new(75.5, 20, -300))); // di belakang kamera
    }

    [Fact]
    public void Fight_ChasesAndAimsAtNearestEnemy()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 2, Hud = HudText.Parse("Easy · Area 1/5 · 2 left"),
            PlayerPos = new(75, 3, -320), CameraLook = (0, -1),
            Camera = new CameraInfo(new(75.508, 20.141, -330.377), new(0, -0.744, -0.668), 70, 1920, 1009),
            Enemies = [new("0", new(62.8, 3, -350), 100, 100), new("1", new(74.8, 3, -340), 100, 100), new("9", new(75, 3, -322), 0, 100)],
        };
        var a = m.Step(snap, 10_000);
        var move = Assert.Single(a.OfType<MoveAction>());
        Assert.Contains(VirtualKeys.W, move.Keys);
        Assert.Contains("#1", Assert.Single(a.OfType<AimAction>()).Reason);
    }

    [Fact]
    public void Fight_ApproachOnlyDashes_ThenAttacks_ThenSwapsWhenAllCooling()
    {
        var (m, _) = New(s => s.Combat = s.Combat with { Skills = CombatSettings.DefaultSkills().Select(k => k with { Enabled = true }).ToList() });
        var far = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 1/5 · 1 left"),
            PlayerPos = new(75, 3, -300), CameraLook = (0, -1),
            Enemies = [new("1", new(75, 3, -340), 100, 100)],
            SkillCooldowns = [new("1", null, false), new("2", null, false), new("3", null, false)],
        };
        var a = m.Step(far, 10_000);
        Assert.DoesNotContain(a, x => x is AttackAction);
        Assert.All(a.OfType<KeyAction>(), k => Assert.Contains("Dash", k.Reason));

        var near = far with { PlayerPos = new(75, 3, -336) };
        var b = m.Step(near, 11_000);
        Assert.DoesNotContain(b, x => x is AttackAction); // skill ditekan → basic attack dilewati tick ini
        Assert.Contains(b.OfType<KeyAction>(), k => k.Reason.Contains("(1)"));
        Assert.Contains(b.OfType<MoveAction>(), mv => mv.Keys.Count > 0 && mv.Reason.Contains("Roaming"));

        var cooling = near with { SkillCooldowns = [new("1", 3, false), new("2", 2, false), new("3", null, true)] };
        // Semua cooldown → berhenti serang dulu (animasi selesai), T baru ditekan setelah 900 ms.
        var c0 = m.Step(cooling, 12_100);
        Assert.DoesNotContain(c0, x => x is AttackAction);
        Assert.Contains(c0.OfType<KeyAction>(), k => k.Reason.Contains("menghindar")); // skill habis → Dash sambil roaming
        Assert.DoesNotContain(c0.OfType<KeyAction>(), k => k.Reason.Contains("(T)"));
        var c = m.Step(cooling, 13_000);
        Assert.Contains(c.OfType<KeyAction>(), k => k.Reason.Contains("(T)"));
        Assert.DoesNotContain(c, x => x is AttackAction);
        // Power berganti (HUD skill siap lagi) → skill dipakai lagi.
        Assert.Contains(m.Step(near, 13_600).OfType<KeyAction>(), k => k.Reason.Contains("(1)") || k.Reason.Contains("(2)") || k.Reason.Contains("(3)"));
        // Skill habis + dekat musuh → roaming melingkar (bergerak) sambil lompat, tidak diam.
        Assert.Contains(c.OfType<MoveAction>(), mv => mv.Keys.Count > 0 && mv.Reason.Contains("Roaming"));
        Assert.Contains(c0.Concat(c).OfType<KeyAction>(), k => k.Reason.Contains("Lompat"));
    }

    [Fact]
    public void Turn_DirectionAndDeadzone()
    {
        // Look -Z (0,-1); kanan kamera = +X (Roblox). Musuh di +X → putar ke kanan (dx > 0).
        Assert.True(FarmMachine.Turn(0, -1, 1, -1, 4) > 0);
        Assert.True(FarmMachine.Turn(0, -1, -1, -1, 4) < 0);
        Assert.Equal(0, FarmMachine.Turn(0, -1, 0.02, -1, 4));
        Assert.Equal(80, FarmMachine.Turn(0, -1, 0, 1, 4)); // di belakang → langkah dibatasi
        // Kamera mendongak (look Y +0,9) ke musuh setinggi kamera → mouse ke bawah (dy > 0); sudah pas → 0.
        Assert.True(FarmMachine.Pitch(new(0, 0.9, -0.43), 0, 0, -10, 4) > 0);
        Assert.Equal(0, FarmMachine.Pitch(new(0, -0.17, -0.98), 0, -1.76, -10, 4));
        // Musuh jauh di atas (bos raksasa) → pitch dibatasi +10°, kamera tetap tidak mendongak.
        Assert.True(FarmMachine.Pitch(new(0, 0.5, -0.86), 0, 50, -5, 4) > 0);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("1.7", 1.7)]
    [InlineData("2", 2.0)]
    [InlineData("0", null)]
    public void ParseCooldown_Works(string text, double? expected) => Assert.Equal(expected, GameReader.ParseCooldown(text));

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
    public void AreaCleared_PicksUpCoinsFirst_ThenGivesUp()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 0, Hud = HudText.Parse("Easy · Area 2/5 · 0 left"),
            PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), NextAreaPos = new Vec3(0, 0, -50),
            PlayerHp = 1, Pickups = [new("cash", new(20, 0, 0)), new("health", new(5, 0, 0))],
        };
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal([VirtualKeys.D], move.Keys); // health dilewati karena darah penuh → koin di +X
        Assert.Contains("koin", m.Status);
        m.Step(snap, 3000);
        m.Step(snap, 6500); // tidak terambil → dilewati
        Assert.Contains(Real(m.Step(snap, 7000)), x => x is MoveAction mv && mv.Keys.SequenceEqual([VirtualKeys.W]));
    }

    [Fact]
    public void LowHp_GrabsHealthBeforeEnemy()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, PlayerPos = new(0, 3, 0), CameraLook = (0, -1), PlayerHp = 0.3,
            Enemies = [new("1", new(0, 3, -40), 100, 100)], Pickups = [new("health", new(-15, 3, 0))],
        };
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal([VirtualKeys.A], move.Keys);
        Assert.Contains("health", m.Status);
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
        // Setelah teleport, lingkaran 80 stud (panel teleport masih tampil) → jalan, bukan teleport ulang.
        var after = panel with { PlayerPos = new Vec3(220, 0, 0) };
        var walk = Real(m.Step(after, 7000));
        Assert.DoesNotContain(walk, x => x is ClickAction);
        Assert.Contains(walk.OfType<MoveAction>(), mv => mv.Keys.Count > 0);
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
        for (; t <= 40_000; t += 500) m.Step(snap, t); // sampai → gagal join → room 2 di-ban
        var a = Real(m.Step(snap, t));
        Assert.DoesNotContain(a, x => x is ClickAction);
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
    public void Lobby_SkipsOccupiedRoom()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1),
            StartRooms = [new StartRoom("1", new Vec3(10, 0, 0), Occupied: true, 3, 4), new StartRoom("2", new Vec3(-30, 0, 0), false)],
        };
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 0))));
        Assert.Equal([VirtualKeys.A], move.Keys); // room 1 terisi → ke room 2 (-X = kiri)
    }

    [Fact]
    public void Lobby_JoinTimeout_TriesNextRoom()
    {
        var (m, _) = New();
        var rooms = new[] { new StartRoom("1", new Vec3(1, 0, 0), false), new StartRoom("2", new Vec3(-30, 0, 0), false) };
        var snap = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartRooms = rooms };
        Assert.Empty(Real(m.Step(snap, 0)));      // sudah di room 1, menunggu menu
        Assert.Empty(Real(m.Step(snap, 3000)));
        Assert.Contains(Real(m.Step(snap, 5500)), a => a is NoteAction n && n.Reason.Contains("room 1"));
        var move = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(snap, 6000))));
        Assert.Equal([VirtualKeys.A], move.Keys); // room 1 di-ban → ke room 2
    }

    [Fact]
    public void Lobby_ActMissingOnMap_ClampsToAvailable()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Act = 5 });
        var snap = new GameSnapshot
        {
            Attached = true, LobbyMenuOpen = true, LobbyMapTag = "Training", LobbyActTag = "Training [Act 1]",
            LobbyActs = [new LobbyEntry("Act 1 Training", false, true, Btn)], LobbyStart = Btn,
        };
        var a = Real(m.Step(snap, 0));
        Assert.Contains(a, x => x is NoteAction n && n.Reason.Contains("pakai Act 1"));
        Assert.Contains(a, x => x is ClickAction c && c.Reason.StartsWith("Difficulty"));
        Assert.Contains(Real(m.Step(snap, 1000)), x => x is ClickAction c && c.Reason.StartsWith("Start"));
    }

    [Fact]
    public void Lobby_AllActs_PicksNextAfterProgress()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Act = 0, ActProgress = new() { ["Hxh"] = 2 } });
        var acts = Enumerable.Range(1, 5).Select(i => new LobbyEntry($"Act {i}", false, i == 1, new GuiRect(i * 100, 100, 50, 20))).ToList();
        var snap = new GameSnapshot { Attached = true, LobbyMenuOpen = true, LobbyMapTag = "Hxh", LobbyActTag = "Hxh [Act 1]", LobbyActs = acts };
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
        var diff = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(right, 100))));
        Assert.Equal(AnimeZeroPaths.DifficultyCenters[2].X, diff.X);
        Assert.Empty(Real(m.Step(right, 300)));                       // tunggu difficulty terdaftar
        var start = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(right, 900))));
        Assert.Equal(Btn.CenterX, start.X);
    }

    [Fact]
    public void Lobby_MapBelowList_ScrollsDown_ThenClicks()
    {
        var (m, _) = New(s => s.Farm = s.Farm with { Map = "Tidal High Rise" });
        var view = new GuiRect(200, 300, 400, 300);                  // daftar tampak y 300..600
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
            Attached = true, LobbyMenuOpen = true, LobbyActTag = "[Act 1]", LobbyDifficultyTag = "EASY", LobbyStart = Btn,
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
        var snap = new GameSnapshot { Attached = true, QueueSkip = skip };
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
        var a = Real(m.Step(new GameSnapshot { Attached = true, EnemyCount = 1, Blessings = offers }, 0));
        var click = Assert.IsType<ClickAction>(Assert.Single(a));
        Assert.Equal(1181 + 176, click.X, 0);
    }

    [Fact]
    public void ImmuneBoss_IsSkipped_WhileSummonsAlive()
    {
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 2, BossTag = "IMMUNE", BossSpawn = new(61.85, 279, -146.55),
            Enemies = [new("0", new(61.85, 281.5, -146.55), 100, 100), new("7", new(47.6, 281.5, -252.7), 100, 100)],
        };
        Assert.Equal("7", Assert.Single(FarmMachine.Targets(snap)).Name);
        Assert.Equal(2, FarmMachine.Targets(snap with { BossTag = "BOSS" }).Count);
    }

    [Fact]
    public void ImmuneBossOnly_WalksIntoArea()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, BossTag = "IMMUNE", BossSpawn = new(61.85, 279, -146.55),
            Enemies = [new("0", new(61.85, 281.5, -146.55), 100, 100)],
            PlayerPos = new(72, 243, -440), CameraLook = (0, 1),
            CurrentArea = new AreaBox(new(61.85, 303.5, -208.55), new(200, 70, 200)),
        };
        Assert.Contains(m.Step(snap, 0), a => a is MoveAction { Keys.Count: > 0 });
        Assert.DoesNotContain(m.Step(snap, 100), a => a is AttackAction);
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

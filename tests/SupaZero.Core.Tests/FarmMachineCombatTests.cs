using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    [Fact]
    public void Fight_NoSkillHud_NoSkillKeys()
    {
        // v0.2.79: without the skill HUD the cooldown state is unknown → basic attack only.
        var (m, _) = New();
        var a = m.Step(new GameSnapshot { Attached = true, EnemyCount = 4, Hud = HudText.Parse("Easy · Area 1/5 · 4 left") }, 10_000);
        Assert.Contains(a, x => x is AttackAction);
        Assert.DoesNotContain(a.OfType<KeyAction>(), k => k.VirtualKey != VirtualKeys.Space);
    }

    [Fact]
    public void Fight_UnreadableCooldownText_NotPressed()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 4, Hud = HudText.Parse("Easy · Area 1/5 · 4 left"),
            SkillCooldowns = [new("1", GameReader.ParseCooldown("??"), false), new("2", null, true), new("3", 2, false)],
        };
        Assert.DoesNotContain(m.Step(snap, 10_000).OfType<KeyAction>(), k => k.VirtualKey != VirtualKeys.Space);
    }

    [Fact]
    public void Fight_AttacksAndRotatesSkills()
    {
        var (m, _) = New();
        var snap = new GameSnapshot { Attached = true, EnemyCount = 4, Hud = HudText.Parse("Easy · Area 1/5 · 4 left"),
            SkillCooldowns = [new("1", null, false), new("2", null, false), new("3", null, false)] };
        var a1 = m.Step(snap, 10_000);
        Assert.DoesNotContain(a1, x => x is AttackAction); // tick with a skill → basic attack skipped
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
        Assert.Contains(VirtualKeys.S, mv.Keys); // enemy 15 studs ahead < 40 → back off
        var far = snap with { Enemies = [new("1", new(0, 3, -80), 100, 100)] };
        var mf = m.Step(far, 10_100).OfType<MoveAction>().First(x => x.Keys.Count > 0);
        Assert.Contains(VirtualKeys.W, mf.Keys); // 80 > 44 → chase
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
        Assert.Empty(m.Step(snap, 10_200).OfType<KeyAction>().Where(k => k.VirtualKey != VirtualKeys.Space && !k.Reason.Contains("Dash"))); // 1 on cooldown, 2 locked, 3 just used
        Assert.Single(m.Step(snap, 10_700).OfType<KeyAction>().Where(k => k.VirtualKey != VirtualKeys.Space && !k.Reason.Contains("Dash"))); // HUD says 3 is ready again
    }

    [Fact]
    public void Camera_ProjectsEnemyFromSnapshot()
    {
        // Snapshot 23 Oct 2026: camera (75.508,20.141,-330.377) look (0,-0.744,-0.668) FOV 70 viewport 1920x1009.
        var cam = new CameraInfo(new(75.508, 20.141, -330.377), new(0, -0.744, -0.668), 70, 1920, 1009);
        var p = cam.WorldToScreen(new(74.8, 3.001, -362));
        Assert.NotNull(p);
        Assert.InRange(p!.Value.X, 930, 960);   // slightly left of center
        Assert.InRange(p.Value.Y, 200, 300);    // farther ahead → upper part of the screen
        Assert.Null(cam.WorldToScreen(new(75.5, 20, -300))); // behind the camera
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
            ClientW = 1920, ClientH = 1009,
            Enemies = [new("0", new(62.8, 3, -350), 100, 100), new("1", new(74.8, 3, -340), 100, 100), new("9", new(75, 3, -322), 0, 100)],
        };
        var a = m.Step(snap, 10_000);
        var move = Assert.Single(a.OfType<MoveAction>());
        Assert.Contains(VirtualKeys.W, move.Keys);
        Assert.Contains("#1", Assert.Single(a.OfType<AimAction>()).Reason);
        // v0.2.79: camera viewport ≠ client area (e.g. DPI scaling) → no cursor aiming.
        var (m2, _) = New();
        Assert.Empty(m2.Step(snap with { ClientW = 1536, ClientH = 807 }, 10_000).OfType<AimAction>());
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
            SkillCooldowns = [new("1", null, false), new("2", null, false), new("3", null, false)], DashCooling = false,
        };
        var a = m.Step(far, 10_000);
        Assert.DoesNotContain(a, x => x is AttackAction);
        Assert.All(a.OfType<KeyAction>(), k => Assert.Contains("Dash", k.Reason));

        var near = far with { PlayerPos = new(75, 3, -336) };
        var b = m.Step(near, 11_000);
        Assert.DoesNotContain(b, x => x is AttackAction); // skill pressed → basic attack skipped this tick
        Assert.Contains(b.OfType<KeyAction>(), k => k.Reason.Contains("(1)"));
        Assert.Contains(b.OfType<MoveAction>(), mv => mv.Keys.Count > 0 && mv.Reason.Contains("Roaming"));

        var cooling = near with { SkillCooldowns = [new("1", 3, false), new("2", 2, false), new("3", null, true)] };
        // All on cooldown → stop attacking first (animation finishes), T pressed only after 900 ms.
        var c0 = m.Step(cooling, 12_100);
        Assert.DoesNotContain(c0, x => x is AttackAction);
        Assert.Contains(c0.OfType<KeyAction>(), k => k.Reason.Contains("evade")); // skills used up → Dash while roaming
        Assert.DoesNotContain(c0.OfType<KeyAction>(), k => k.Reason.Contains("(T)"));
        var c = m.Step(cooling, 13_000);
        Assert.Contains(c.OfType<KeyAction>(), k => k.Reason.Contains("(T)"));
        Assert.DoesNotContain(c, x => x is AttackAction);
        // Power swapped (HUD skills ready again) → skills used again.
        Assert.Contains(m.Step(near, 13_600).OfType<KeyAction>(), k => k.Reason.Contains("(1)") || k.Reason.Contains("(2)") || k.Reason.Contains("(3)"));
        // Skills used up + near enemy → circular roaming (moving) while jumping, not standing still.
        Assert.Contains(c.OfType<MoveAction>(), mv => mv.Keys.Count > 0 && mv.Reason.Contains("Roaming"));
        Assert.Contains(c0.Concat(c).OfType<KeyAction>(), k => k.Reason.Contains("Jump"));
    }

    [Fact]
    public void Fight_PressesUltimateG_BetweenSkills_AndOldSettingsGetUltRow()
    {
        var old = new CombatSettings(Skills: CombatSettings.DefaultSkills().Where(k => k.Name != "Ultimate").ToList(), UltBossOnly: false);
        Assert.Contains(old.EffectiveSkills, k => k.Name == "Ultimate" && k.Key == "G" && k.Enabled);
        var (m, _) = New(s => s.Combat = old);
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 1/5 · 1 left"),
            PlayerPos = new(75, 3, -336), CameraLook = (0, -1),
            Enemies = [new("1", new(75, 3, -340), 100, 100)],
            SkillCooldowns = [new("1", 3, false), new("2", 2, false), new("3", 4, false)],
        };
        // HUD ult locked → G not pressed; ready → G pressed first even though skill 1 is ready.
        Assert.DoesNotContain(m.Step(snap with { UltReady = false }, 10_000).OfType<KeyAction>(), k => k.Reason.Contains("Ultimate (G)"));
        var ready = snap with { UltReady = true, SkillCooldowns = [new("1", null, false), new("2", 2, false), new("3", 4, false)] };
        Assert.Contains(m.Step(ready, 11_000).OfType<KeyAction>(), k => k.Reason.Contains("Ultimate (G)"));
        Assert.DoesNotContain(m.Step(ready, 11_500).OfType<KeyAction>(), k => k.Reason.Contains("Ultimate (G)"));
    }

    [Fact]
    public void Fight_UltBossOnly_PressesGOnlyWhenBossBarShownAndNotImmune()
    {
        var (m, _) = New(s => s.Combat = new CombatSettings());
        Assert.True(new CombatSettings().UltBossOnly);
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 1/5 · 1 left"),
            PlayerPos = new(75, 3, -336), CameraLook = (0, -1), UltReady = true,
            Enemies = [new("1", new(75, 3, -340), 100, 100)],
            SkillCooldowns = [new("1", 3, false), new("2", 2, false), new("3", 4, false)],
        };
        Assert.DoesNotContain(m.Step(snap, 10_000).OfType<KeyAction>(), k => k.Reason.Contains("Ultimate (G)"));
        Assert.DoesNotContain(m.Step(snap with { BossBarVisible = true, BossTag = "[IMMUNE]" }, 11_000).OfType<KeyAction>(), k => k.Reason.Contains("Ultimate (G)"));
        Assert.Contains(m.Step(snap with { BossBarVisible = true, BossTag = "[BOSS]" }, 12_000).OfType<KeyAction>(), k => k.Reason.Contains("Ultimate (G)"));
    }

    [Fact]
    public void Carry_IdlesOnceDamageReached_AndParsesScoreboard()
    {
        Assert.Equal(5712, GameReader.ParseDamage("5,712"));
        Assert.Equal(11300, GameReader.ParseDamage("11.3K"));
        Assert.Equal(0, GameReader.ParseDamage("0"));
        Assert.Null(GameReader.ParseDamage("abc"));
        var (m, _) = New(s => s.Combat = new CombatSettings(CarryDamage: 1000));
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 1/5 · 1 left"),
            PlayerPos = new(75, 3, -336), CameraLook = (0, -1),
            Enemies = [new("1", new(75, 3, -340), 100, 100)],
            SkillCooldowns = [new("1", null, false)],
        };
        Assert.Contains(m.Step(snap with { MyDamage = 400 }, 10_000).OfType<KeyAction>(), k => k.Reason.Contains("Skill 1"));
        var idle = m.Step(snap with { MyDamage = 1200 }, 11_000);
        Assert.DoesNotContain(idle.OfType<KeyAction>(), k => true);
        Assert.Contains("Carry", m.Status);
    }

    [Fact]
    public void Turn_DirectionAndDeadzone()
    {
        // Look -Z (0,-1); camera right = +X (Roblox). Enemy at +X → turn right (dx > 0).
        Assert.True(FarmMachine.Turn(0, -1, 1, -1, 4) > 0);
        Assert.True(FarmMachine.Turn(0, -1, -1, -1, 4) < 0);
        Assert.Equal(0, FarmMachine.Turn(0, -1, 0.02, -1, 4));
        Assert.Equal(80, FarmMachine.Turn(0, -1, 0, 1, 4)); // behind → step is clamped
        // Camera looking up (look Y +0.9) at an enemy at camera height → mouse down (dy > 0); already aligned → 0.
        Assert.True(FarmMachine.Pitch(new(0, 0.9, -0.43), 0, 0, -10, 4) > 0);
        Assert.Equal(0, FarmMachine.Pitch(new(0, -0.17, -0.98), 0, -1.76, -10, 4));
        // Enemy far above (giant boss) → pitch clamped to +10°, camera still does not look up.
        Assert.True(FarmMachine.Pitch(new(0, 0.5, -0.86), 0, 50, -5, 4) > 0);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("1.7", 1.7)]
    [InlineData("2", 2.0)]
    [InlineData("0", null)]
    [InlineData("abc", double.NaN)]
    public void ParseCooldown_Works(string text, double? expected) => Assert.Equal(expected, GameReader.ParseCooldown(text));

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
    public void ImmuneBoss_IsSkipped_WhileSummonsAlive()
    {
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 2, BossTag = "IMMUNE", BossSpawn = new(61.85, 279, -146.55),
            Enemies = [new("0", new(61.85, 281.5, -146.55), 100, 100, 2), new("7", new(62, 281.5, -150), 100, 100)],
        };
        // v0.2.81: the skipped one = the model with 2 Highlights, even though a summon stands near BossSpawn.
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
            Enemies = [new("0", new(61.85, 281.5, -146.55), 100, 100, 2)],
            PlayerPos = new(72, 243, -440), CameraLook = (0, 1),
            CurrentArea = new AreaBox(new(61.85, 303.5, -208.55), new(200, 70, 200)),
        };
        Assert.Contains(m.Step(snap, 0), a => a is MoveAction { Keys.Count: > 0 });
        Assert.DoesNotContain(m.Step(snap, 100), a => a is AttackAction);
    }

    [Theory]
    [InlineData(GameScreen.InRound, 3, true)]
    [InlineData(GameScreen.BlessingPick, 2, true)]
    [InlineData(GameScreen.InRound, 0, false)]
    [InlineData(GameScreen.Lobby, 3, false)]
    public void ShiftLock_RequiredWheneverEnemiesDetected(GameScreen screen, int enemies, bool must)
    {
        Assert.Equal(must, MacroRunner.MustShiftLock(screen, enemies));
    }

    [Fact]
    public void Arise_WalksToCorpse_ThenHoldsE()
    {
        var (m, _) = New();
        m.SetSelectedMapForTest("Shadow Gate");
        var far = new GameSnapshot
        {
            Attached = true, EnemyCount = 2, Hud = HudText.Parse("Easy · Area 1/5 · 2 left"),
            PlayerPos = new(0, 3, 0), CameraLook = (0, -1), Enemies = [new("1", new(0, 3, -15), 100, 100)],
            Arises = [new AriseInfo("Suit Leg", new(0, 0.1, 20), 0.9, 10)], Shadows = (0, 3),
        };
        var a = m.Step(far, 10_000);
        Assert.Contains(a, x => x is MoveAction { Keys.Count: > 0 });
        Assert.DoesNotContain(a.OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
        var near = far with { PlayerPos = new(0, 3, 18) };
        var e = Assert.Single(m.Step(near, 10_500).OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
        Assert.Equal(1150, e.HoldMs);
        // v0.2.79: Shadows go up → Arise declared successful, that corpse is not visited again.
        var done = m.Step(near with { Shadows = (1, 3) }, 11_000);
        Assert.Contains(done.OfType<NoteAction>(), n => n.Reason.Contains("Arise succeeded"));
        Assert.DoesNotContain(m.Step(near with { Shadows = (1, 3) }, 13_000).OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
    }

    [Fact]
    public void Arise_ShadowsUnreadable_Skipped()
    {
        var (m, _) = New();
        m.SetSelectedMapForTest("Shadow Gate");
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 2, Hud = HudText.Parse("Easy · Area 1/5 · 2 left"),
            PlayerPos = new(0, 3, 18), CameraLook = (0, -1), Enemies = [new("1", new(0, 3, -15), 100, 100)],
            Arises = [new AriseInfo("Suit Leg", new(0, 0.1, 20), 0.9, 10)],
        };
        Assert.DoesNotContain(m.Step(snap, 10_000).OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
    }

    [Fact]
    public void Arise_Disabled_Ignored()
    {
        var (m, _) = New(s => s.Combat = s.Combat with { AutoArise = false });
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 1/5 · 1 left"),
            PlayerPos = new(0, 3, 0), CameraLook = (0, -1), Enemies = [new("1", new(0, 3, -5), 100, 100)],
            Arises = [new AriseInfo("Suit Leg", new(0, 0.1, 2), 0.9, 10)],
        };
        Assert.DoesNotContain(m.Step(snap, 10_000).OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
    }

    private static GameSnapshot AriseNear(double y) => new()
    {
        Attached = true, EnemyCount = 2, Hud = HudText.Parse("Nightmare · Area 1/5 · 2 left"),
        PlayerPos = new(0, y, 18), CameraLook = (0, -1), Enemies = [new("1", new(0, 3, -15), 100, 100)],
        Arises = [new AriseInfo("Suit Leg", new(0, 0.1, 20), 0.9, 10)], Shadows = (0, 3),
    };

    [Fact]
    public void Arise_OtherMapOrUnknownMap_Skipped()
    {
        // v0.3.19 (Byzan 27 Oct 2026): Arise only on Shadow Gate; unknown map = no Arise.
        var (m, _) = New();
        Assert.DoesNotContain(m.Step(AriseNear(3), 10_000).OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
        m.SetSelectedMapForTest("Hxh");
        var a = m.Step(AriseNear(3), 10_500);
        Assert.DoesNotContain(a.OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
        Assert.Contains(a.OfType<NoteAction>(), n => n.Reason.Contains("only runs on Shadow Gate"));
    }

    [Fact]
    public void Arise_WhileAirborne_WaitsToLand_NoJump()
    {
        // v0.3.19 (Byzan 27 Oct 2026): holding E mid-jump does not raise the corpse → land first, never press Space during Arise.
        var (m, _) = New();
        m.SetSelectedMapForTest("Shadow Gate");
        var air1 = m.Step(AriseNear(5.5), 10_000);
        var air2 = m.Step(AriseNear(4.2), 10_150);
        foreach (var a in air1.Concat(air2))
        {
            Assert.False(a is KeyAction { VirtualKey: VirtualKeys.E or VirtualKeys.Space }, $"unexpected key {a}");
        }
        Assert.Contains(air2, x => x is MoveAction { Keys.Count: 0 });
        // Landed: Y back at 3 and stable ≥ 300 ms → hold E.
        Assert.DoesNotContain(m.Step(AriseNear(3), 10_300).OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
        var e = Assert.Single(m.Step(AriseNear(3), 10_650).OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
        Assert.Equal(1150, e.HoldMs);
        // Between holds: stand still, no jump.
        Assert.DoesNotContain(m.Step(AriseNear(3), 11_000).OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.Space);
    }
}

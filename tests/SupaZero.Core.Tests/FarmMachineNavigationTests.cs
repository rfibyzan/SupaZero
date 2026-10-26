using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
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
        // Stuck for 2 s → jump + attack obstacle
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
        Assert.Equal([VirtualKeys.D], move.Keys); // health skipped because HP is full → coin at +X
        Assert.Contains("coin", m.Status);
        m.Step(snap, 3000);
        m.Step(snap, 6500); // not picked up → skipped
        Assert.Contains(Real(m.Step(snap, 7000)), x => x is MoveAction mv && mv.Keys.SequenceEqual([VirtualKeys.W]));
    }

    [Theory]
    [InlineData("Nightmare · Enter area 4/5", 4, true)]
    [InlineData("Enter area 5/5", 5, true)]
    [InlineData("Easy · Area 3/5 · 0 left", 4, false)]
    [InlineData("Easy · Area 5/5 · 0 left", 0, false)]
    public void Hud_EnterArea_TargetsThatArea(string text, int target, bool enter)
    {
        var h = HudText.Parse(text)!;
        Assert.Equal(enter, h.Enter);
        Assert.Equal(target, h.TargetArea);
    }

    [Fact]
    public void AreaEntryPoint_Hxh_Area4_EdgeNearSpawn()
    {
        // Snapshot S1: Spawns.4 (158,0.5,-198.5), Areas.4 center (453.905,25,-200.97) size 299.536×80×131.551.
        var e = GameReader.AreaEntryPoint(new Vec3(453.905, 25, -200.97), new Vec3(299.536, 80, 131.551), new Vec3(158, 0.5, -198.5));
        Assert.InRange(e.X, 311, 313);   // west edge of Area 4 (304.1) + 8
        Assert.Equal(-198.5, e.Z, 3);
        Assert.InRange(Math.Sqrt((e.X - 158) * (e.X - 158)), 145, 160); // ≈ marker "AREA 4 150 studs"
    }
}

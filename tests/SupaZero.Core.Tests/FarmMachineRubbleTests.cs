using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    [Fact]
    public void RubblePending_AttackedAfterWaveCleared_NotBefore()
    {
        // v0.3.12 (Byzan 26 Oct): rubble exists from the start; after the Area 2 wave is clear → attack the rubble, not walk to the area point.
        var (m, _) = New();
        var rub = new Vec3(60, 3, 0);
        var fight = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 2/5 · 1 left"), PlayerPos = new(0, 3, 0), CameraLook = (0, -1),
            Enemies = [new("1", new(0, 3, -10), 100, 100)], RubblePending = rub, NextAreaPos = new(0, 3, -200),
        };
        // Before the wave is seen: pending rubble is not attacked.
        var pre = fight with { EnemyCount = 0, Enemies = [], Hud = HudText.Parse("Easy · Area 2/5 · 0 left") };
        var (m0, _) = New();
        m0.Step(pre, 10_000);
        Assert.DoesNotContain("rubble", m0.Status, StringComparison.OrdinalIgnoreCase);
        m.Step(fight, 10_000);
        var cleared = fight with { EnemyCount = 0, Enemies = [new("1", new(0, 3, -10), 0, 100)], Hud = HudText.Parse("Easy · Area 2/5 · 0 left") };
        var a = m.Step(cleared, 11_000);
        Assert.Contains("rubble", m.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(a, x => x is MoveAction { Keys.Count: > 0 });
        // Rubble destroyed (Blocker no longer solid → RubblePending null) → continue to the next area.
        m.Step(cleared with { RubblePending = null }, 12_000);
        Assert.DoesNotContain("rubble", m.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RubblePending_EnterNextArea_OnlyWhenRubbleBlocksTheWay()
    {
        // v0.3.29: "Enter area 3/5" after area 2's wave; rubble beyond area 3's entry = later area → walk to area 3, not to the rubble.
        Vec3 Rub(double z) => new(0, 3, z);
        GameSnapshot Fight() => new()
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 2/5 · 1 left"), PlayerPos = new(0, 3, 0), CameraLook = (0, -1),
            Enemies = [new("1", new(0, 3, -10), 100, 100)], NextAreaPos = new(0, 3, -89),
        };
        foreach (var (z, expectRubble) in new[] { (-150.0, false), (-40.0, true) })
        {
            var (m, _) = New();
            m.Step(Fight() with { RubblePending = Rub(z) }, 10_000);
            var enter = Fight() with { EnemyCount = 0, Enemies = [], RubblePending = Rub(z), Hud = HudText.Parse("Enter area 3/5") };
            m.Step(enter, 11_000);
            Assert.Equal(expectRubble, m.Status.Contains("rubble", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Rubble_IsTreatedAsEnemyTarget()
    {
        var s = new GameSnapshot { Attached = true, EnemyCount = 1, Enemies = [new EnemyInfo("Rubble", new Vec3(5, 0, 5), 1, 1)], RubblePos = new Vec3(5, 0, 5) };
        Assert.Single(FarmMachine.Targets(s));
    }
}

public class WalkableMoundTests
{
    [Theory]
    [InlineData("DIRT_MOUND_1", "MAP", true)]
    [InlineData("DIRT_MOUND_1", "Rubble", false)]
    [InlineData("ROCK_PATCH_A", "MAP", false)]
    [InlineData("DES_ROAD", "MAP", true)]
    [InlineData("INT_ROAD_1", "MAP", true)]
    public void DirtMound_OutsideRubble_IsWalkable(string name, string parent, bool walkable) =>
        Assert.Equal(walkable, SupaZero.Core.Game.GameReader.IsWalkableMound(name, parent));

    [Fact]
    public void NoBlock_NeverBlocks_ButStaysFloor()
    {
        var road = SupaZero.Core.Game.NavRect.FromPart(new(0, 9, 0), new(244, 6, 160)) with { NoBlock = true };
        Assert.False(road.BlocksBodyAt(8.9));
        Assert.True(road.IsFloorAt(10));
    }
}

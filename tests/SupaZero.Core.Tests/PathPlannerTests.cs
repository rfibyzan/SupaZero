using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public class PathPlannerTests
{
    // Area 100×60; dinding tengah x -30..52 (z -2..2) → jalur atas ke bawah harus memutar lewat kiri (huruf U).
    private static NavMap UMap() => new(
        [NavRect.FromPart(new(0, 3, 0), new(100, 80, 60))],
        [NavRect.FromPart(new(11, 3, 0), new(82, 1, 4))],
        []);

    [Fact]
    public void Find_GoesAroundWall()
    {
        var path = new PathPlanner().Find(UMap(), new(40, 3, -15), new(40, 3, 15));
        Assert.NotNull(path);
        Assert.Contains(path!, w => w.X < -28);
        Assert.Equal(new Vec3(40, 3, 15), path![^1]);
    }

    [Fact]
    public void Clear_StraightLineOpen()
    {
        var pl = new PathPlanner();
        Assert.True(pl.Clear(UMap(), new(40, 3, -15), new(-20, 3, -20)));
        Assert.False(pl.Clear(UMap(), new(40, 3, -15), new(40, 3, 15)));
    }

    [Fact]
    public void RotatedObstacleRespected()
    {
        // Dinding diputar 90° (LookVector +X): Size.Z searah X.
        var r = NavRect.FromPart(new(0, 0, 0), new(4, 1, 40), 1, 0);
        Assert.True(r.Contains(15, 0));
        Assert.False(r.Contains(0, 15));
    }

    [Fact]
    public void Farm_AreaCleared_FollowsPathNotStraight()
    {
        var m = new FarmMachine(() => new AppSettings());
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 0, Hud = HudText.Parse("Easy · Area 2/5 · 0 left"),
            PlayerPos = new Vec3(40, 3, -15), CameraLook = (0, -1), NextAreaPos = new Vec3(40, 3, 15), Nav = UMap(),
        };
        var move = Assert.IsType<MoveAction>(Assert.Single(m.Step(snap, 0), a => a is MoveAction { Keys.Count: > 0 }));
        Assert.Contains(VirtualKeys.A, move.Keys); // ke kiri (-X) dulu, bukan lurus S menabrak dinding
    }
}

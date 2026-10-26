using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public class PathPlannerTests
{
    // Area 100×60; middle wall x -30..52 (z -2..2) → a top-to-bottom path must detour via the left (U shape).
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
    public void Solids_WallBlocks_FloorAndRampIgnored()
    {
        var area = NavRect.FromPart(new(0, 3, 0), new(100, 80, 60));
        var wall = NavRect.FromPart(new(11, 18, 0), new(82, 60, 5));            // like AlleyBarrier (see-through, CanCollide)
        var floor = NavRect.FromPart(new(0, -0.5, 0), new(200, 1, 200));       // Baseplate: below the feet
        var ramp = NavRect.FromPart(new(-40, 3, 0), new(10, 1, 20), 0, -0.97, 0.26); // sloped
        var map = new NavMap([area], [], [], [wall, floor, ramp]);
        var path = new PathPlanner().Find(map, new(40, 3, -15), new(40, 3, 15));
        Assert.NotNull(path);
        Assert.Contains(path!, w => w.X < -28);
        Assert.True(new PathPlanner().Clear(new NavMap([area], [], [], [floor, ramp]), new(40, 3, -15), new(40, 3, 15)));
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
        // Wall rotated 90° (LookVector +X): Size.Z along X.
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
        Assert.Contains(VirtualKeys.A, move.Keys); // left (-X) first, not straight S into the wall
    }

    // ---------- v0.2.47: real Hxh map (Byzan snapshot S1, stuck point Area 4 → 5) ----------
    private static NavMap HxhS1(bool staleBorder4Rubble = false)
    {
        var w = new List<NavRect>(); var o = new List<NavRect>(); var so = new List<NavRect>();
        var files = new List<string> { "hxh_s1_navmap.txt" };
        if (staleBorder4Rubble) files.Add("hxh_border4_rubble.txt");
        foreach (var line in files.SelectMany(f => File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", f))))
        {
            if (line.StartsWith('#') || line.Length < 3) continue;
            var f = line.Split(' ');
            double D(int i) => double.Parse(f[i], System.Globalization.CultureInfo.InvariantCulture);
            var r = NavRect.FromPart(new(D(1), D(2), D(3)), new(D(4), D(5), D(6)), D(7), D(9), D(8));
            (f[0] switch { "W" => w, "O" => o, _ => so }).Add(r);
        }
        return new NavMap(w, o, [], so);
    }

    [Fact]
    public void Hxh_Area4To5_LoopsAroundBlock_NotIntoAlley()
    {
        var map = HxhS1();
        var pl = new PathPlanner();
        var from = new Vec3(305, 4.5, -141.6);   // in front of AlleyBarrier4 (11.6 studs) as in Byzan's screenshot
        var entry = new Vec3(295, 2, -53.3);     // World.AreaEntries.5 (marker "AREA 5 87 studs")
        Assert.False(pl.Clear(map, from, entry)); // straight north = dead-end alley (AlleyBarrier4 + gap between buildings)
        var path = pl.Find(map, from, entry);
        Assert.NotNull(path);
        Assert.False(pl.Partial, pl.LastFailure);
        Assert.True(pl.UsedOutside);
        Assert.Contains(path!, p => p.X > 560);  // detours via the east road
        Assert.Equal(entry, path![^1]);
        // No waypoint enters the AlleyBarrier4 alley (x 301..309, z -135..-96).
        Assert.DoesNotContain(path!, p => p.X is > 298 and < 312 && p.Z is > -135 and < -96);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    public void Hxh_Area4To5_FromEastStreet_WorksWhileJumping(double y)
    {
        // Byzan log 24 Oct 2026 20:54 (Shadow Gate 5S, same map as Hxh): from (535,-193) partial path into a dead-end alley while HRP was airborne.
        var map = HxhS1(); var pl = new PathPlanner();
        var entry = new Vec3(295, 2, -53.3);
        var path = pl.Find(map, new Vec3(535, y, -193), entry);
        Assert.NotNull(path);
        Assert.False(pl.Partial, pl.LastFailure);
        Assert.Equal(entry, path![^1]);
        Assert.DoesNotContain(path!, p => p.X is > 298 and < 312 && p.Z is > -135 and < -96);
    }

    [Theory]
    [InlineData(291, -148)]
    [InlineData(289, -148)]
    public void Hxh_Area4To5_Log1046_StaleRubbleBlocksEastRoad(double x, double z)
    {
        // Byzan log 25 Oct 10:46: "from (291,-148) to (295,-53), not found; map 5 areas, 730 obstacles", Border4 open.
        // 730 = 707 (S1 map) + 23 CanCollide parts of Map.Border4.Rubble (snapshot 00:57:52) already removed by the game → stale cache.
        var entry = new Vec3(295, 2, -53.3);
        var stale = HxhS1(staleBorder4Rubble: true);
        Assert.Equal(730, stale.Solids!.Count);
        var pl = new PathPlanner();
        var bad = pl.Find(stale, new Vec3(x, 4.5, z), entry);
        Assert.True(bad is null || pl.Partial); // stale rubble blocks the east road = live symptom
        var good = new PathPlanner().Find(HxhS1(), new Vec3(x, 4.5, z), entry);
        Assert.NotNull(good);
        Assert.Contains(good!, p => p.X > 560);
        Assert.Equal(entry, good![^1]);
    }

    // ---------- v0.3.10: Evil Reflection spawn (Byzan World snapshot 07:43:42, 26 Oct 2026) ----------
    private static NavMap Er0743()
    {
        var w = new List<NavRect>(); var so = new List<NavRect>(); var nodes = new List<Vec3>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "er_0743_world.txt")))
        {
            if (line.StartsWith('#') || line.Length < 3) continue;
            var f = line.Split(' ');
            double D(int i) => double.Parse(f[i], System.Globalization.CultureInfo.InvariantCulture);
            if (f[0] == "N") { nodes.Add(new(D(1), D(2), D(3))); continue; }
            var r = NavRect.FromPart(new(D(1), D(2), D(3)), new(D(4), D(5), D(6)), D(7), D(9), D(8));
            (f[0] == "W" ? w : so).Add(r);
        }
        return new NavMap(w, [], nodes, so);
    }

    [Theory]
    [InlineData(242.5)]
    [InlineData(243)]
    [InlineData(244)]
    public void Er_SpawnBelowStairs_GridFails_ButTargetIsOtherLevel(double y)
    {
        // Log 07:43:42: "from (72,-440) to (62,-209), not found: character position (72,-440) blocked by obstacles; map 1 area, 185 obstacles".
        var map = Er0743();
        Assert.Equal(185, map.Solids!.Count);
        var from = new Vec3(72, y, -440);
        var target = new Vec3(61.85, 303.53, -208.55); // World.Areas.1 center
        Assert.Null(new PathPlanner().Find(map, from, target)); // the single-floor grid does fail (live symptom)
        Assert.Equal(240, PathPlanner.FootY(map, from), 0);     // Model#5.Plane.006 top 239.98
        Assert.True(FarmMachine.OtherLevel(map, from, target)); // → walk straight up the stairs, not stand still
    }

    [Fact]
    public void Hxh_EntryIsSameLevel_NoStraightFallback()
    {
        var map = HxhS1();
        Assert.False(FarmMachine.OtherLevel(map, new Vec3(291, 4.5, -148), new Vec3(295, 2, -53.3)));
    }
}

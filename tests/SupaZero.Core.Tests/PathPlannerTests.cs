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
    public void Solids_WallBlocks_FloorAndRampIgnored()
    {
        var area = NavRect.FromPart(new(0, 3, 0), new(100, 80, 60));
        var wall = NavRect.FromPart(new(11, 18, 0), new(82, 60, 5));            // seperti AlleyBarrier (tembus pandang, CanCollide)
        var floor = NavRect.FromPart(new(0, -0.5, 0), new(200, 1, 200));       // Baseplate: di bawah kaki
        var ramp = NavRect.FromPart(new(-40, 3, 0), new(10, 1, 20), 0, -0.97, 0.26); // miring
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

    // ---------- v0.2.47: peta asli Hxh (snapshot S1 Byzan, titik nyangkut Area 4 → 5) ----------
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
        var from = new Vec3(305, 4.5, -141.6);   // depan AlleyBarrier4 (11,6 stud) seperti screenshot Byzan
        var entry = new Vec3(295, 2, -53.3);     // World.AreaEntries.5 (penanda "AREA 5 87 studs")
        Assert.False(pl.Clear(map, from, entry)); // lurus ke utara = gang buntu (AlleyBarrier4 + celah bangunan)
        var path = pl.Find(map, from, entry);
        Assert.NotNull(path);
        Assert.False(pl.Partial, pl.LastFailure);
        Assert.True(pl.UsedOutside);
        Assert.Contains(path!, p => p.X > 560);  // memutar lewat jalan timur
        Assert.Equal(entry, path![^1]);
        // Tidak ada titik belok yang masuk gang AlleyBarrier4 (x 301..309, z -135..-96).
        Assert.DoesNotContain(path!, p => p.X is > 298 and < 312 && p.Z is > -135 and < -96);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    public void Hxh_Area4To5_FromEastStreet_WorksWhileJumping(double y)
    {
        // Log Byzan 24 Okt 2026 20:54 (Shadow Gate 5S, peta sama dengan Hxh): dari (535,-193) parsial ke gang buntu saat HRP di udara.
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
        // Log Byzan 25 Okt 10:46: "dari (291,-148) ke (295,-53), tidak ketemu; peta 5 area, 730 rintangan", Border4 terbuka.
        // 730 = 707 (peta S1) + 23 part CanCollide Map.Border4.Rubble (snapshot 00:57:52) yang sudah dihapus game → cache basi.
        var entry = new Vec3(295, 2, -53.3);
        var stale = HxhS1(staleBorder4Rubble: true);
        Assert.Equal(730, stale.Solids!.Count);
        var pl = new PathPlanner();
        var bad = pl.Find(stale, new Vec3(x, 4.5, z), entry);
        Assert.True(bad is null || pl.Partial); // rubble basi menutup jalan timur = gejala live
        var good = new PathPlanner().Find(HxhS1(), new Vec3(x, 4.5, z), entry);
        Assert.NotNull(good);
        Assert.Contains(good!, p => p.X > 560);
        Assert.Equal(entry, good![^1]);
    }
}

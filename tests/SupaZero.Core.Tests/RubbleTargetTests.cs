using SupaZero.Core.Game;
using Xunit;

public class RubbleTargetTests
{
    [Fact]
    public void RubbleTarget_Hxh_Border2_NearestWallPoint()
    {
        // Snapshot 24 Okt 2026: Blocker (-14.97,26.014,-99.864) size (101.551,57,4), pemain (-16.768,3,-89.622).
        var t = GameReader.RubbleTarget(new(-14.97, 26.014, -99.864), new(101.551, 57, 4), new(-16.768, 3, -89.622));
        Assert.Equal(-16.768, t.X, 2);
        Assert.Equal(-97.864, t.Z, 2);
        Assert.Equal(3, t.Y, 2);
    }
}

public class RubbleTargetRotatedTests
{
    [Fact]
    public void RubbleTarget_Hxh_Border4_Rotated_FollowsLookVector()
    {
        // Snapshot RIE 25 Okt 2026 00:59:13: Border4.Blocker (607.136,20.014,-200.97) size (131.551,57,4) LookVector (1,0,0)
        // → dinding membentang di sumbu Z (-266.7..-135.2), tebal di sumbu X (605.1..609.1).
        var me = new SupaZero.Core.Settings.Vec3(560, 3, -150);
        var t = GameReader.RubbleTarget(new(607.136, 20.014, -200.97), new(131.551, 57, 4), me, 1, 0);
        Assert.Equal(605.136, t.X, 2);
        Assert.Equal(-150, t.Z, 2);
        // Dulu (rata sumbu) hasilnya x = 560, z ≈ -198.97: titik di tengah Area 4, jauh dari dinding.
    }

    [Fact]
    public void RubbleTarget_ClimbStop_Border4_FrontFace()
    {
        // ClimbStop (585.112,100,-195.072) size (11.996,200,2) LookVector (-1,0,0): muka barat x = 584.112.
        var t = GameReader.RubbleTarget(new(585.112, 100, -195.072), new(11.996, 200, 2), new(500, 3, -195), -1, 0);
        Assert.Equal(584.112, t.X, 2);
        Assert.Equal(-195, t.Z, 2);
    }
}

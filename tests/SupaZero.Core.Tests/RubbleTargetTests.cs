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

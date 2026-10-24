using SupaZero.Core.Settings;
using Xunit;

namespace SupaZero.Core.Tests;

public class OverlayPlacementTests
{
    [Fact]
    public void TopRightDefaultSticksToRightEdgeBelowTopbar()
    {
        var (x, y) = OverlayPlacement.ToScreen((100, 50, 1920, 1009), 1, 0, 300, 230);
        Assert.Equal(100 + 1920 - 12 - 300, x);
        Assert.Equal(50 + 64, y);
    }

    [Fact]
    public void BottomLeftStaysInsideClient()
    {
        var (x, y) = OverlayPlacement.ToScreen((0, 0, 1920, 1009), 0, 1, 300, 230);
        Assert.Equal(12, x);
        Assert.Equal(1009 - 12 - 230, y);
    }

    [Fact]
    public void RelativeRoundTripsAndClamps()
    {
        Assert.Equal(0.5, OverlayPlacement.ToRelative(OverlayPlacement.ToOffset(0.5, 216, 34), 216, 34), 3);
        Assert.Equal(1, OverlayPlacement.ToRelative(999, 216, 34));
        Assert.Equal(0, OverlayPlacement.ToRelative(-5, 216, 34));
    }

    [Fact]
    public void StaleStartVoteWithEnemiesIsInRound()
    {
        var s = new SupaZero.Core.Game.GameSnapshot { Attached = true, StartVoteVisible = true, EnemyCount = 3 };
        Assert.Equal(SupaZero.Core.Game.GameScreen.InRound, s.Screen);
        Assert.Equal(SupaZero.Core.Game.GameScreen.StartVote, (s with { EnemyCount = 0 }).Screen);
        // v0.2.71: sisa model di Workspace.Enemies (EnemyCount 2, daftar kosong) tapi HUD "Easy · Get ready!" → tetap vote.
        Assert.Equal(SupaZero.Core.Game.GameScreen.StartVote, (s with { EnemyCount = 2, HudRaw = "Easy · Get ready!" }).Screen);
    }
}

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
        // v0.2.71: leftover models in Workspace.Enemies (EnemyCount 2, empty list) but HUD "Easy · Get ready!" → still vote.
        Assert.Equal(SupaZero.Core.Game.GameScreen.StartVote, (s with { EnemyCount = 2, HudRaw = "Easy · Get ready!" }).Screen);
    }
}

public class SettingsMigrationTests
{
    [Fact]
    public void Load_RenamesLegacyIndonesianSwapSkill()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sz_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Combat":{"Skills":[{"Name":"Ganti power","Key":"T","Enabled":false,"CooldownMs":9000}]}}""");
        var s = SupaZero.Core.Settings.AppSettings.Load(path);
        File.Delete(path);
        var k = Assert.Single(s.Combat.Skills!);
        Assert.Equal("Swap power", k.Name); Assert.Equal("T", k.Key); Assert.False(k.Enabled); Assert.Equal(9000, k.CooldownMs);
    }
}

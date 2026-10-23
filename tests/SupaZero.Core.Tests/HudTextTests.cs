using SupaZero.Core.Game;

namespace SupaZero.Core.Tests;

public class HudTextTests
{
    [Fact]
    public void ParsesAreaAndEnemies()
    {
        var h = HudText.Parse("Easy · Area 3/5 · 8 left")!;
        Assert.Equal("Easy", h.Difficulty);
        Assert.Equal(3, h.Area);
        Assert.Equal(5, h.AreaTotal);
        Assert.Equal(8, h.EnemiesLeft);
        Assert.False(h.IsFinalArea);
    }

    [Fact]
    public void FinalArea() => Assert.True(HudText.Parse("Nightmare · Area 5/5 · 1 left")!.IsFinalArea);

    [Fact]
    public void StoryComplete() => Assert.True(HudText.Parse("Story complete")!.StoryComplete);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    public void Garbage(string? s) => Assert.Null(HudText.Parse(s));
}

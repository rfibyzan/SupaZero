using SupaZero.Core.Automation;
using SupaZero.Core.Game;

namespace SupaZero.Core.Tests;

public class CarryRotationTests
{
    [Fact]
    public void NeedsInput_FromReadStateOnly()
    {
        var round = new GameSnapshot { Attached = true, EnemyCount = 3, Hud = HudText.Parse("Easy · Area 1/5 · 3 left") };
        Assert.Equal(GameScreen.InRound, round.Screen);
        Assert.True(CarryRotation.NeedsInput(round with { MyDamage = 400 }, 1000));
        Assert.False(CarryRotation.NeedsInput(round with { MyDamage = 1000 }, 1000));
        Assert.True(CarryRotation.NeedsInput(round with { MyDamage = null }, 1000));   // unreadable = fights normally
        Assert.True(CarryRotation.NeedsInput(round with { MyDamage = 5000 }, 0));      // carry off
        Assert.False(CarryRotation.NeedsInput(new GameSnapshot(), 1000));              // not attached / unknown
        Assert.False(CarryRotation.NeedsInput(null, 1000));
    }

    [Fact]
    public void Pick_SliceThenLongestWaiting()
    {
        // current (0) needs input, slice running → stay
        Assert.Equal(0, CarryRotation.Pick([(true, 0), (true, 0)], 0, 1000, 0));
        // slice over, other needs → switch
        Assert.Equal(1, CarryRotation.Pick([(true, 5000), (true, 0)], 0, 5000, 0));
        // slice over but nobody else needs → stay
        Assert.Equal(0, CarryRotation.Pick([(true, 5000), (false, 0)], 0, 5000, 0));
        // current idle (damage reached) → longest waiting of the others
        Assert.Equal(2, CarryRotation.Pick([(false, 9000), (true, 800), (true, 300)], 0, 9000, 8000));
        // nobody needs → -1 (keep front)
        Assert.Equal(-1, CarryRotation.Pick([(false, 0), (false, 0)], 0, 9000, 0));
    }
}

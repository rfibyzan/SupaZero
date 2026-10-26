using SupaZero.Core.Automation;

namespace SupaZero.Core.Tests;

public class NavigatorTests
{
    [Theory]
    [InlineData(0, -10, new ushort[] { VirtualKeys.W })]          // forward (look -Z)
    [InlineData(0, 10, new ushort[] { VirtualKeys.S })]           // back
    [InlineData(10, 0, new ushort[] { VirtualKeys.D })]           // right
    [InlineData(-10, 0, new ushort[] { VirtualKeys.A })]          // left
    [InlineData(10, -10, new ushort[] { VirtualKeys.W, VirtualKeys.D })]
    public void EightDirections(double tx, double tz, ushort[] expected) =>
        Assert.Equal(expected, Navigator.KeysToward(0, 0, 0, -1, tx, tz, 2));

    [Fact]
    public void Arrived_NoKeys() => Assert.Empty(Navigator.KeysToward(0, 0, 0, -1, 1, 1, 3));
}

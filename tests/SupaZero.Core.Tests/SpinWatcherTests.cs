using SupaZero.Core.Game;
using Xunit;

public class SpinWatcherTests
{
    private static PowerSpinObs P(bool run, string? status, string? res, int rolls, int lucky, bool target = false) =>
        new(run, status, res, "Mythic", target, rolls, lucky, ["Mythical Pity: 2/175"]);

    [Fact]
    public void PowerAutoSpinReportsAfterStatusDisappears()
    {
        var w = new SpinWatcher(); var t = new DateTime(2026, 10, 24, 9, 0, 0);
        Assert.Null(w.ObservePower(P(false, null, "A", 619, 2344), t));
        Assert.Null(w.ObservePower(P(true, "Auto Spinning: Lucky Spin  •  Spin 1  •  Stops at Mythic+", "A", 619, 2344), t.AddSeconds(2)));
        Assert.Null(w.ObservePower(P(true, "Auto Spinning: Lucky Spin  •  Spin 5  •  Stops at Mythic+", "B", 619, 2340), t.AddSeconds(10)));
        Assert.Null(w.ObservePower(P(false, null, "Cataclysm", 619, 2339, true), t.AddSeconds(12)));
        var r = w.ObservePower(P(false, null, "Cataclysm", 619, 2339, true), t.AddSeconds(14));
        Assert.NotNull(r);
        Assert.Equal("Cataclysm", r!.Result);
        Assert.Equal(5, r.Spins);
        Assert.Equal(5, r.Used);
        Assert.Equal("Lucky Spin", r.Mode);
        Assert.Equal("Mythic+", r.StopAt);
        Assert.True(r.StoppedOnTarget);
        Assert.Null(w.ObservePower(P(false, null, "Cataclysm", 619, 2339), t.AddSeconds(16)));
    }

    [Fact]
    public void TraitReportsWhenAutoRerollStops()
    {
        var w = new SpinWatcher(); var t = new DateTime(2026, 10, 24, 9, 0, 0);
        TraitSpinObs O(bool run, string trait, int dice) => new(run, "Mochi Commander", trait, "0.039%", ["+12%"], dice);
        Assert.Null(w.ObserveTrait(O(false, "A", 1900), t));
        Assert.Null(w.ObserveTrait(O(true, "B", 1900), t.AddSeconds(2)));
        Assert.Null(w.ObserveTrait(O(true, "C", 1880), t.AddSeconds(30)));
        Assert.Null(w.ObserveTrait(O(false, "Sniper", 1878), t.AddSeconds(32)));
        var r = w.ObserveTrait(O(false, "Sniper", 1878), t.AddSeconds(34));
        Assert.NotNull(r);
        Assert.Equal("Sniper", r!.Result);
        Assert.Equal(22, r.Used);
        Assert.Equal(1878, r.LeftAfter);
        Assert.Null(w.ObserveTrait(O(false, "Sniper", 1878), t.AddSeconds(36)));
    }
}

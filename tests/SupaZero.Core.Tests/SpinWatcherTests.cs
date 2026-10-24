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
    public void TraitReportsAfterIdleOnlyWhenRerolledRepeatedly()
    {
        var w = new SpinWatcher(); var t = new DateTime(2026, 10, 24, 9, 0, 0);
        TraitSpinObs O(string trait) => new(true, "Cursed King", trait, "1%", []);
        Assert.Null(w.ObserveTrait(O("A"), t));
        Assert.Null(w.ObserveTrait(O("B"), t.AddSeconds(2)));
        Assert.Null(w.ObserveTrait(O("B"), t.AddSeconds(20)));   // 1 perubahan = reroll manual, tidak dilaporkan
        Assert.Null(w.ObserveTrait(O("C"), t.AddSeconds(22)));
        Assert.Null(w.ObserveTrait(O("D"), t.AddSeconds(24)));
        Assert.Null(w.ObserveTrait(O("D"), t.AddSeconds(28)));
        var r = w.ObserveTrait(O("D"), t.AddSeconds(33));
        Assert.NotNull(r);
        Assert.Equal("D", r!.Result);
        Assert.Null(w.ObserveTrait(O("D"), t.AddSeconds(50)));
    }
}

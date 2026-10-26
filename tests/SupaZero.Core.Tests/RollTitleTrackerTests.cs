using SupaZero.Core.Game;
using Xunit;

public class RollTitleTrackerTests
{
    [Fact]
    public void EmptyRollList_TitleAcceptedOnlyAfterChangeOnSwitch()
    {
        // Snapshot 08:19:35: Roll list empty, SkillTree Slot5 selected, Title "Twisted Agony".
        var t = new RollTitleTracker();
        Assert.Null(t.Resolve(1, "Shadow Lord", false, null));         // start: not proven yet
        Assert.Null(t.Resolve(5, "Shadow Lord", false, null));         // slot switched, title not changed yet (stale)
        Assert.Equal("Twisted Agony", t.Resolve(5, "Twisted Agony", false, null));
        Assert.Equal("Twisted Agony", t.Resolve(5, "Twisted Agony", false, null));
    }

    [Fact]
    public void EmptyRollList_SameTitleAfterSwitch_StaysUnknown()
    {
        var t = new RollTitleTracker();
        t.Resolve(1, "Shadow Lord", false, null);
        Assert.Null(t.Resolve(2, "Shadow Lord", false, null));
    }

    [Fact]
    public void RollListPresent_RequiresMatchingSlot()
    {
        var t = new RollTitleTracker();
        Assert.Equal("Divine Emperor", t.Resolve(2, "Divine Emperor", true, 2));
        Assert.Null(t.Resolve(3, "Divine Emperor", true, 2));
    }
}

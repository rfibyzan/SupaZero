using SupaZero.Core.Automation;
using SupaZero.Core.Features.Codes;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public class CodesTests
{
    [Fact]
    public void ParseBeebom_ActiveListOnly()
    {
        string html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "beebom-anime-zero-codes.html"));
        var codes = CodeList.ParseBeebom(html);
        Assert.Equal("ANEWCHAPTER", codes[0]);
        Assert.Contains("SORRYXD67", codes);
        Assert.Contains("RELEASE", codes);
        Assert.Equal(33, codes.Count);
        Assert.Empty(CodeList.ParseBeebom("<p>nothing</p>"));
        Assert.Equal(["a1b", "XYZ9"], CodeList.ParseManual("a1b, XYZ9 ;bad-code x"));
    }

    [Theory]
    [InlineData("Code \"SORRYXD67\" redeemed!", CodeOutcome.Redeemed)]
    [InlineData("You received  $2,500", CodeOutcome.Redeemed)]
    [InlineData("You have already used this code.", CodeOutcome.AlreadyUsed)]
    [InlineData("You must be a member of the group to redeem this code!", CodeOutcome.Failed)]
    [InlineData("Invalid code", CodeOutcome.Failed)]
    public void Classify_Toasts(string text, CodeOutcome want) => Assert.Equal(want, CodeList.Classify(text));

    [Fact]
    public void Machine_OpensTypesVerifiesRedeemsAndCloses()
    {
        var s = new AppSettings();
        var m = new FarmMachine(() => s) { CodesFor = _ => ["MINICODE"] };
        var tried = new List<(string, string, CodeOutcome)>();
        m.CodeTried = (p, c, o, _) => tried.Add((p, c, o));
        var lobby = new GameSnapshot { Attached = true, LocalPlayer = "AuraTrout", CodesButton = new GuiRect(1711, 410, 88, 106) };
        Assert.Contains(m.Step(lobby, 0).OfType<ClickAction>(), c => c.Reason == "Codes button");

        var menu = new GameSnapshot
        {
            Attached = true, LocalPlayer = "AuraTrout", CodesOpen = true, CodesText = "",
            CodesTextBox = new GuiRect(624, 525, 672, 58), CodesRedeem = new GuiRect(622, 607, 675, 63), CodesExit = new GuiRect(1242, 386, 68, 68),
            Notifications = [new NotificationInfo(1, "You received  $2,500")],
        };
        Assert.Equal(GameScreen.CodesMenu, menu.Screen);
        Assert.Contains(m.Step(menu, 1000).OfType<ClickAction>(), c => c.Reason == "Code box");
        var type = Assert.Single(m.Step(menu, 1400).OfType<TypeTextAction>());
        Assert.True(type.Unicode);
        // Box text readable and equal → Redeem.
        var typed = menu with { CodesText = "MINICODE" };
        Assert.Contains(m.Step(typed, 2000).OfType<ClickAction>(), c => c.Reason == "Redeem MINICODE");
        // Old toast (Id 1) is ignored; the new one decides.
        Assert.Empty(tried);
        m.Step(typed with { Notifications = [new NotificationInfo(1, "You received  $2,500"), new NotificationInfo(7, "Code \"MINICODE\" redeemed!")] }, 2600);
        Assert.Equal(("AuraTrout", "MINICODE", CodeOutcome.Redeemed), Assert.Single(tried));
        // All done → Exit.
        Assert.Contains(m.Step(typed, 5000).OfType<ClickAction>(), c => c.Reason == "Codes Exit");
    }

    [Fact]
    public void Machine_FocusedBoxReadsEmpty_RedeemsAnyway()
    {
        var s = new AppSettings();
        var m = new FarmMachine(() => s) { CodesFor = _ => ["THANKS4WAITING"] };
        var menu = new GameSnapshot { Attached = true, LocalPlayer = "P", CodesOpen = true, CodesText = "", CodesTextBox = new GuiRect(0, 0, 100, 40), CodesRedeem = new GuiRect(0, 50, 100, 40), CodesExit = new GuiRect(0, 100, 40, 40) };
        m.Step(menu, 0); m.Step(menu, 400);
        Assert.Contains(m.Step(menu, 1000).OfType<ClickAction>(), c => c.Reason == "Redeem THANKS4WAITING");
    }

    [Fact]
    public void Machine_TypingNotAccepted_RetriesWithKeysThenSkips()
    {
        var s = new AppSettings();
        var m = new FarmMachine(() => s) { CodesFor = _ => ["ABC1"] };
        CodeOutcome? got = null;
        m.CodeTried = (_, _, o, _) => got = o;
        var menu = new GameSnapshot { Attached = true, LocalPlayer = "P", CodesOpen = true, CodesText = "abX", CodesTextBox = new GuiRect(0, 0, 100, 40), CodesRedeem = new GuiRect(0, 50, 100, 40), CodesExit = new GuiRect(0, 100, 40, 40) };
        m.Step(menu, 0); m.Step(menu, 400); m.Step(menu, 2000);           // click, type (unicode), mismatch
        m.Step(menu, 2100);
        var second = m.Step(menu, 2500).OfType<TypeTextAction>().Single(); // retype with plain keys
        Assert.False(second.Unicode);
        m.Step(menu, 4200);
        Assert.Equal(CodeOutcome.Failed, got);
        Assert.Contains(m.Step(menu, 6000).OfType<ClickAction>(), c => c.Reason == "Codes Exit");
    }

    [Fact]
    public void Machine_NoCodesOrOff_LobbyUnchanged()
    {
        var s = new AppSettings();
        s.Farm = s.Farm with { AutoRedeemCodes = false };
        var m = new FarmMachine(() => s) { CodesFor = _ => ["X1Y"] };
        var lobby = new GameSnapshot { Attached = true, LocalPlayer = "P", CodesButton = new GuiRect(1711, 410, 88, 106) };
        Assert.DoesNotContain(m.Step(lobby, 0).OfType<ClickAction>(), c => c.Reason == "Codes button");
        // Menu open with nothing to do → close it.
        Assert.Contains(m.Step(new GameSnapshot { Attached = true, CodesOpen = true, CodesExit = new GuiRect(0, 0, 40, 40) }, 3000).OfType<ClickAction>(), c => c.Reason == "Codes Exit");
    }
}

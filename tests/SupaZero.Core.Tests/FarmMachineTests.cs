using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    private static readonly GuiRect Btn = new(100, 100, 40, 20);

    /// <summary>Buang MoveAction kosong (lepas tombol) yang ditambahkan tiap tick.</summary>
    private static List<MacroAction> Real(IReadOnlyList<MacroAction> a) => a.Where(x => x is not MoveAction { Keys.Count: 0 }).ToList();

    private static (FarmMachine M, AppSettings S) New(Action<AppSettings>? cfg = null)
    {
        var s = new AppSettings();
        cfg?.Invoke(s);
        return (new FarmMachine(() => s), s);
    }

    [Fact]
    public void NotAttached_NoActions()
    {
        var (m, _) = New();
        Assert.Empty(Real(m.Step(new GameSnapshot(), 0)));
    }
}

using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    // ---------- v0.2.49 ----------
    [Fact]
    public void Lobby_PowerCheck_OpensTraitThenSpin_ThenWalks()
    {
        var (m, _) = New();
        var btns = new List<LobbyEntry> { new("Trait", false, false, new GuiRect(10, 300, 80, 80)), new("Spin", false, false, new GuiRect(10, 400, 80, 80)) };
        var lobby = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)], HubMenuButtons = btns, ClientW = 1920, ClientH = 1080 };
        var c1 = Assert.IsType<ClickAction>(Assert.Single(Real(m.Step(lobby, 0))));
        Assert.Equal(50, c1.X); Assert.Equal(340, c1.Y);              // tombol Trait
        Assert.Empty(Real(m.Step(lobby with { TraitMenuOpen = true }, 500)));  // membaca
        var close = Real(m.Step(lobby with { TraitMenuOpen = true }, 3600));
        Assert.Contains(close, a => a is ClickAction { Y: 340 });      // tutup Trait (klik tombol yang sama)
        var c2 = Real(m.Step(lobby, 4000));
        Assert.Contains(c2, a => a is ClickAction { Y: 440 });         // tombol Spin
        m.Step(lobby with { SpinMenuOpen = true }, 4500);
        Assert.Contains(Real(m.Step(lobby with { SpinMenuOpen = true }, 8000)), a => a is ClickAction { Y: 440 });
        var walk = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(lobby, 8500))));
        Assert.Equal([VirtualKeys.D], walk.Keys);                      // lanjut jalan ke lingkaran
    }

    [Fact]
    public void RollExit_SurfaceGui_ShiftedByScreenGuiOffset()
    {
        // Snapshot PlayerGui 26 Okt 08:58:55: Roll (ScreenGui) AbsolutePosition 0,58; Roll.SurfaceGui 0,0;
        // MainFrame.ExitButton.HitPad 18.343,864.269 ukuran 220.595×61.244 → pusat layar Y = 894.89 + 58 = 952.9 (Exit di screenshot ≈ 945/999·1009).
        Assert.Equal(952.891, GameReader.SurfaceToScreenY(864.269 + 61.244 / 2, 58, 0), 3);
    }

    [Fact]
    public void Lobby_SpinningCheck_ClicksExitUntilRollCloses()
    {
        var (m, _) = New();
        var spin = new GuiRect(20, 638, 186, 70);
        var exit = new GuiRect(18, 922, 220, 61);
        var lobby = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)], SpinButton = spin, ClientW = 1920, ClientH = 1080 };
        Assert.Contains(Real(m.Step(lobby, 0)), a => a is ClickAction { Y: 673 });            // tombol Spinning
        var open = lobby with { SpinMenuOpen = true, SpinButton = spin with { X = -262 }, RollExit = exit };
        Assert.Empty(Real(m.Step(open, 500)));                                                  // membaca
        Assert.Contains(Real(m.Step(open, 2100)), a => a is ClickAction { Y: 952.5 });         // Exit #1
        Assert.DoesNotContain(Real(m.Step(open, 2600)), a => a is ClickAction);               // jeda 1,5 dtk
        Assert.Contains(Real(m.Step(open, 3700)), a => a is ClickAction { Y: 952.5 });         // masih terbuka → Exit #2
        var walk = Assert.IsType<MoveAction>(Assert.Single(Real(m.Step(lobby, 4200))));       // tertutup → lanjut
        Assert.Equal([VirtualKeys.D], walk.Keys);
    }

    [Fact]
    public void Lobby_PowerCheck_MenuNeverOpens_SkipsAfterTimeout()
    {
        var (m, _) = New();
        var btns = new List<LobbyEntry> { new("Trait", false, false, new GuiRect(10, 300, 80, 80)) };
        var lobby = new GameSnapshot { Attached = true, PlayerPos = new Vec3(0, 0, 0), CameraLook = (0, -1), StartCircles = [new Vec3(30, 0, 0)], HubMenuButtons = btns, ClientW = 1920, ClientH = 1080 };
        m.Step(lobby, 0);
        var after = Real(m.Step(lobby, FarmMachine.PowerMenuTimeoutMs + 100));
        Assert.DoesNotContain(after, a => a is ClickAction);
        Assert.Contains(Real(m.Step(lobby, FarmMachine.PowerMenuTimeoutMs + 300)), a => a is MoveAction { Keys.Count: > 0 });
    }
}

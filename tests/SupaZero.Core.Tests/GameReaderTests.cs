using RIE.Core.Properties;
using RIE.Core.Roblox;
using SupaZero.Core.Game;

namespace SupaZero.Core.Tests;

public class GameReaderTests
{
    [Fact]
    public void ReadsHudEnemiesAndEndScreen()
    {
        var f = new FakeRoblox();
        var (dm, ws, players, lp, pg, _) = f.BuildGame();
        var reader = new InstanceReader(f.Mem, f.Offsets);
        var game = new GameReader(reader, new PropertyReader(reader));

        // RoundHUD.Container.TopPart.RemainingFrame.EnemyCount
        long hud = f.New("RoundHUD", "ScreenGui", pg);
        long cont = f.New("Container", "Frame", hud);
        long top = f.New("TopPart", "Frame", cont);
        long rem = f.New("RemainingFrame", "Frame", top);
        long ec = f.New("EnemyCount", "TextLabel", rem);
        foreach (long g in new[] { cont, top, rem, ec }) f.Mem.WriteByte(g + FakeRoblox.VisibleOff, 1);
        f.SetText(ec, "Easy · Area 2/5 · 3 left");

        long enemies = f.New("enemies", "Folder", ws);
        f.New("1", "Model", enemies);
        f.New("2", "Model", enemies);

        var roots = new DataModelLocator(reader).Locate(f.ModuleBase);
        var snap = game.Read(roots);
        Assert.Equal(2, snap.Hud!.Area);
        Assert.Equal(2, snap.EnemyCount);
        Assert.Equal(GameScreen.InRound, snap.Screen);

        // EndScreen terlihat dengan ResultText DEFEAT
        long endGui = f.New("EndGui", "ScreenGui", pg);
        long end = f.New("EndScreen", "Frame", endGui);
        long res = f.New("ResultText", "TextLabel", end);
        f.Mem.WriteByte(end + FakeRoblox.VisibleOff, 1);
        f.Mem.WriteByte(res + FakeRoblox.VisibleOff, 1);
        f.SetText(res, "DEFEAT");
        Assert.Equal(GameScreen.Defeat, game.Read(roots).Screen);

        // Disembunyikan → bukan layar akhir lagi
        f.Mem.WriteByte(end + FakeRoblox.VisibleOff, 0);
        Assert.Equal(GameScreen.InRound, game.Read(roots).Screen);
    }
}

public class ShadowsParseTests
{
    [Theory]
    [InlineData("2/3", 2, 3)]
    [InlineData(" 3 / 3 ", 3, 3)]
    public void Parses(string t, int c, int m) => Assert.Equal((c, m), SupaZero.Core.Game.GameReader.ParseShadows(t));
    [Fact] public void Null() => Assert.Null(SupaZero.Core.Game.GameReader.ParseShadows("Shadows"));
}

public class ShiftLockCameraTests
{
    private static SupaZero.Core.Game.GameSnapshot Snap(double camX, double camZ) => new()
    {
        PlayerPos = new SupaZero.Core.Settings.Vec3(0, 3, 0),
        // kamera menghadap -Z (utara); kanan = +X
        Camera = new SupaZero.Core.Game.CameraInfo(new SupaZero.Core.Settings.Vec3(camX, 8, camZ), new SupaZero.Core.Settings.Vec3(0, -0.3, -1), 70, 1920, 1009),
    };

    [Fact] public void Locked_OffsetRight() => Assert.True(Snap(1.75, 12).ShiftLockFromCamera);
    [Fact] public void Unlocked_Centered() => Assert.False(Snap(0.1, 12).ShiftLockFromCamera);
    [Fact] public void Ambiguous_IsNull() => Assert.Null(Snap(0.8, 12).ShiftLockFromCamera);

    [Fact]
    public void Cutscene_SubjectNotPlayer_IsNull()
    {
        var s = Snap(1.75, 12) with { LocalPlayer = "Astakun17" };
        s = s with { Camera = s.Camera! with { Subject = "Workspace.Effects.KaiselDragon" } };
        Assert.False(s.CameraFollowsPlayer);
        Assert.Null(s.ShiftLockFromCamera);
    }

    [Fact]
    public void Subject_PlayerHumanoid_Follows()
    {
        var s = Snap(1.75, 12) with { LocalPlayer = "Astakun17" };
        s = s with { Camera = s.Camera! with { Subject = "Workspace.Astakun17.Humanoid" } };
        Assert.True(s.ShiftLockFromCamera);
    }

    [Fact] public void CameraFar_IsNull() => Assert.Null(Snap(1.75, 80).ShiftLockFromCamera);
}

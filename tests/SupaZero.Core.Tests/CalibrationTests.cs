using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public class CalibrationTests
{
    [Fact]
    public void NotAttached_SingleFail()
    {
        var r = Calibration.Run(new GameSnapshot(), new AppSettings());
        Assert.Equal(CheckState.Fail, Assert.Single(r).State);
    }

    [Fact]
    public void InRound_ReportsStageEnemiesSkills()
    {
        var s = new GameSnapshot
        {
            Attached = true, PlaceId = AnimeZeroPaths.PlaceId, LocalPlayer = "AuralNova", EnemyCount = 1,
            Hud = HudText.Parse("Easy · Area 1/5 · 1 left"), PlayerPos = new(0, 3, 0),
            Camera = new(new(0, 10, 10), new(0, -0.5, -0.86), 70, 1920, 1009),
            Enemies = [new("7", new(0, 3, -10), 80, 100)],
            SkillCooldowns = [new("1", null, false), new("2", 3, false), new("3", null, true)], DashCooling = false,
        };
        var r = Calibration.Run(s, new AppSettings());
        Assert.Contains(r, i => i.Name == "Stage" && i.State == CheckState.Ok);
        Assert.Contains(r, i => i.Name == "Enemies" && i.Value.Contains("#7"));
        Assert.Contains(r, i => i.Name == "Skill HUD" && i.Value.Contains("3: locked"));
        Assert.DoesNotContain(r, i => i.State == CheckState.Fail);
    }
}

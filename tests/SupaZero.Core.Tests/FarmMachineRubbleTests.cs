using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

public partial class FarmMachineTests
{
    [Fact]
    public void RubblePending_AttackedAfterWaveCleared_NotBefore()
    {
        // v0.3.12 (Byzan 26 Okt): rubble ada sejak awal; setelah wave Area 2 bersih → serang rubble, bukan jalan ke titik area.
        var (m, _) = New();
        var rub = new Vec3(60, 3, 0);
        var fight = new GameSnapshot
        {
            Attached = true, EnemyCount = 1, Hud = HudText.Parse("Easy · Area 2/5 · 1 left"), PlayerPos = new(0, 3, 0), CameraLook = (0, -1),
            Enemies = [new("1", new(0, 3, -10), 100, 100)], RubblePending = rub, NextAreaPos = new(0, 3, -200),
        };
        // Sebelum wave terlihat: rubble menunggu tidak diserang.
        var pre = fight with { EnemyCount = 0, Enemies = [], Hud = HudText.Parse("Easy · Area 2/5 · 0 left") };
        var (m0, _) = New();
        m0.Step(pre, 10_000);
        Assert.DoesNotContain("rubble", m0.Status, StringComparison.OrdinalIgnoreCase);
        m.Step(fight, 10_000);
        var cleared = fight with { EnemyCount = 0, Enemies = [new("1", new(0, 3, -10), 0, 100)], Hud = HudText.Parse("Easy · Area 2/5 · 0 left") };
        var a = m.Step(cleared, 11_000);
        Assert.Contains("rubble", m.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(a, x => x is MoveAction { Keys.Count: > 0 });
        // Rubble hancur (Blocker tidak padat → RubblePending null) → lanjut ke area berikutnya.
        m.Step(cleared with { RubblePending = null }, 12_000);
        Assert.DoesNotContain("rubble", m.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rubble_IsTreatedAsEnemyTarget()
    {
        var s = new GameSnapshot { Attached = true, EnemyCount = 1, Enemies = [new EnemyInfo("Rubble", new Vec3(5, 0, 5), 1, 1)], RubblePos = new Vec3(5, 0, 5) };
        Assert.Single(FarmMachine.Targets(s));
    }
}

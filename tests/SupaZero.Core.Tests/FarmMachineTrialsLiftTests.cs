using SupaZero.Core.Automation;
using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Tests;

/// <summary>v0.3.18 Trials Lift (snapshot World 09:31:14): spawn (-175,-86.2,33), Lift (-50,-84.1,33), prompt "Ascend" E, MaxActivationDistance 14.</summary>
public partial class FarmMachineTests
{
    private static GameSnapshot TrialsArena(bool liftOn, Vec3 me) => new()
    {
        Attached = true, EnemyCount = 0, PlayerPos = me, CameraLook = (1, 0),
        Lift = new LiftInfo(new(-50, -84.12, 33), liftOn, 14, "Ascend"),
    };

    [Fact]
    public void TrialsLift_Disabled_WaitsWithoutPressingE()
    {
        var (m, _) = New();
        var a = Real(m.Step(TrialsArena(false, new(-175, -86.19, 33)), 0));
        Assert.DoesNotContain(a, x => x is KeyAction { VirtualKey: VirtualKeys.E });
        Assert.Contains("waiting for the next wave", m.Status);
    }

    [Fact]
    public void TrialsLift_Enabled_WalksToLift_ThenTapsE()
    {
        var (m, _) = New();
        var far = Real(m.Step(TrialsArena(true, new(-175, -86.19, 33)), 0));
        Assert.Contains(far, x => x is MoveAction { Keys.Count: > 0 });
        Assert.DoesNotContain(far, x => x is KeyAction { VirtualKey: VirtualKeys.E });
        var near = Real(m.Step(TrialsArena(true, new(-53, -86.19, 33)), 3000));
        Assert.Contains(near, x => x is KeyAction { VirtualKey: VirtualKeys.E } k && k.Reason.Contains("Ascend"));
        Assert.DoesNotContain(Real(m.Step(TrialsArena(true, new(-53, -86.19, 33)), 3500)), x => x is KeyAction { VirtualKey: VirtualKeys.E });
    }
}

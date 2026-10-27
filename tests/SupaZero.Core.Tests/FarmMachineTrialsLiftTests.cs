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

    // v0.3.42 Spirit's Bathtub (snapshot World 00:39:38): Markers.Exit (-171,7,-43.5) + ExitPrompt, HUD "Head to the elevator to continue!".
    private static GameSnapshot Bathtub(bool on, Vec3 me, double hold = 0) => new()
    {
        Attached = true, EnemyCount = 0, PlayerPos = me, CameraLook = (1, 0), HudRaw = "Head to the elevator to continue!",
        Lift = new LiftInfo(new(-171, 7, -43.5), on, 10, "Elevator", hold, Elevator: true),
    };

    [Fact]
    public void Elevator_Disabled_WalksThere_NeverPressesE()
    {
        var (m, _) = New();
        var far = Real(m.Step(Bathtub(false, new(-130, 2, -40)), 0));
        Assert.Contains(far, x => x is MoveAction { Keys.Count: > 0 });
        var near = Real(m.Step(Bathtub(false, new(-169, 2, -43)), 3000));
        Assert.DoesNotContain(near, x => x is KeyAction { VirtualKey: VirtualKeys.E });
    }

    [Fact]
    public void Elevator_Enabled_HoldsEForHoldDuration()
    {
        var (m, _) = New();
        var near = Real(m.Step(Bathtub(true, new(-169, 2, -43), hold: 1), 0));
        var e = Assert.Single(near.OfType<KeyAction>(), k => k.VirtualKey == VirtualKeys.E);
        Assert.Equal(1250, e.HoldMs);
    }
}

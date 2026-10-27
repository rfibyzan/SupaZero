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

    [Fact]
    public void ElevatorRoute_FollowsRecordedStairs()
    {
        var r = ElevatorRoutes.For(new(383, 98, -57))!;
        Assert.NotNull(r);
        Assert.Equal(-1, ElevatorRoutes.Next(r, new(413, 19.7, -60)));       // ground, away from the stairs
        Assert.Equal(4, ElevatorRoutes.Next(r, new(435, 19.7, -89.4)));      // at the stair foot → next point
        int mid = ElevatorRoutes.Next(r, new(440, 51.3, -45));               // landing between points 10 and 11
        Assert.Equal(10, mid);
        Assert.Equal(r.Points.Count, ElevatorRoutes.Next(r, new(384.5, 95.5, -57.5)));
        Assert.Equal(-1, ElevatorRoutes.Next(r, new(440, 19.7, -45)));       // under the landing, wrong floor
        Assert.Null(ElevatorRoutes.For(new(-171, 7, -43.5)));
        Assert.True(ElevatorRoutes.Next(r, new(441, 36 + 6, -71)) > 0);      // on the first flight, mid-jump → still on the route
        Assert.Equal(4, ElevatorRoutes.Next(r, new(442, 19.7, -79)));        // under the flight → back to the foot side (point 5)
        Assert.Equal(4, ElevatorRoutes.Next(r, new(438, 19.7, -88)));        // near the foot → point 5                // floor 1 elevator needs no route
    }

    [Fact]
    public void Elevator_Floor2_OnStairs_WalksUp_NoE()
    {
        var (m, _) = New();
        var snap = new GameSnapshot
        {
            Attached = true, EnemyCount = 0, PlayerPos = new(440, 39, -68), CameraLook = (1, 0), HudRaw = "Head to the elevator to continue!",
            Lift = new LiftInfo(new(383, 98, -57), true, 14, "Enter", 0, Elevator: true),
        };
        var a = Real(m.Step(snap, 0));
        Assert.Contains(a, x => x is MoveAction { Keys.Count: > 0 });
        Assert.DoesNotContain(a, x => x is KeyAction { VirtualKey: VirtualKeys.E });
        Assert.True(m.Status.Contains("Climbing the stairs"), m.Status);
    }
}

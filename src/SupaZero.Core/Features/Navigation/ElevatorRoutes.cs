using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>
/// v0.3.43: recorded walking routes to an elevator that sits on another floor. Spirit's Bathtub floor 2 (snapshot World 00:45:04):
/// Markers.Exit (383,98,-57) at the top of the spiral stair mesh S-bathhouse_STAIRS, floor Y≈19.7, Navigation only covers the ground floor.
/// Route = HumanoidRootPart positions Byzan recorded walking up by hand (RIE Watch 28 Oct 00:48:12-00:48:23), ending in front of the elevator.
/// </summary>
public static class ElevatorRoutes
{
    public sealed record Route(string Name, Vec3 Exit, IReadOnlyList<Vec3> Points);

    public static readonly IReadOnlyList<Route> All =
    [
        new("Spirit's Bathtub floor 2 stairs", new(383, 98, -57),
        [
            new(434.8, 19.7, -89.5), new(441.3, 20.6, -86.8), new(442.8, 26.5, -80.8), new(440.4, 32.4, -74.7), new(440.2, 39.0, -68.0),
            new(440.0, 51.2, -54.1), new(439.8, 51.4, -36.6), new(438.1, 51.4, -19.0), new(430.1, 52.5, -17.8), new(424.7, 58.1, -20.4),
            new(418.6, 64.1, -21.8), new(412.7, 70.1, -22.3), new(406.7, 76.3, -22.5), new(399.4, 82.5, -22.7), new(390.7, 82.7, -23.6),
            new(384.0, 84.4, -29.0), new(384.0, 89.2, -35.9), new(386.8, 94.0, -43.6), new(388.5, 95.5, -52.4), new(384.5, 95.5, -57.5),
        ]),
    ];

    /// <summary>Route whose exit is within 4 studs of <paramref name="exit"/>, else null (unknown elevator = no route).</summary>
    public static Route? For(Vec3 exit) => All.FirstOrDefault(r =>
        Math.Abs(r.Exit.X - exit.X) <= 4 && Math.Abs(r.Exit.Y - exit.Y) <= 4 && Math.Abs(r.Exit.Z - exit.Z) <= 4);

    private static double D3(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + 4 * (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    private static double D2(Vec3 a, Vec3 b) => Navigator.Distance2D(a.X, a.Z, b.X, b.Z);

    /// <summary>Index of the next route point to walk to, or -1 when the player is not on/near the route
    /// (nearest point more than 12 studs away, height counted double). Count = route finished.</summary>
    public static int Next(Route r, Vec3 me)
    {
        var pts = r.Points;
        int best = 0;
        for (int i = 1; i < pts.Count; i++) if (D3(me, pts[i]) < D3(me, pts[best])) best = i;
        if (D3(me, pts[best]) > 12) return -1;
        if (best + 1 >= pts.Count) return D2(me, pts[best]) <= 2.5 ? pts.Count : best;
        // Already at this point, or already past it toward the next one → aim for the next point.
        if (D2(me, pts[best]) <= 2.5 || D2(me, pts[best + 1]) < D2(pts[best], pts[best + 1])) return best + 1;
        return best;
    }
}

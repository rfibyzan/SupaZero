namespace SupaZero.Core.Game;

/// <summary>
/// Oriented rectangle in the XZ plane (Part: position, Size X/Z, yaw from LookVector). Size.Z along LookVector,
/// Size.X along RightVector (= Look × Up).
/// </summary>
public sealed record NavRect(double X, double Z, double HalfX, double HalfZ, double LookX, double LookZ, double Y, double HalfY = 0, bool Tilted = false, bool NoBlock = false)
{
    /// <summary>v0.3.46 (Byzan 28 Oct 00:58, Bathtub floor 2 still "Path to stairs not found"; RIE World 00:45:04): the hall floor is
    /// Map.Part (412.6,15.7,-56.6) Size (2, 91.3, 73.8) rolled 90° (Size.Y horizontal). Read without UpVector it became a 91-stud-tall wall
    /// across the hall. With an UpVector that is not vertical (|up.Y| &lt; 0.9) the part's world box is computed from all three axes.</summary>
    public static NavRect FromPart(Settings.Vec3 pos, Settings.Vec3 size, double lookX, double lookZ, double lookY, Settings.Vec3? up)
    {
        if (up is null || Math.Abs(up.Y) >= 0.9) return FromPart(pos, size, lookX, lookZ, lookY);
        // Columns: R = right (Size.X), U = up (Size.Y), B = -look (Size.Z); R = U × B.
        double bx = -lookX, by = -lookY, bz = -lookZ;
        double rx = up.Y * bz - up.Z * by, ry = up.Z * bx - up.X * bz, rz = up.X * by - up.Y * bx;
        double sx = Math.Abs(size.X) / 2, sy = Math.Abs(size.Y) / 2, sz = Math.Abs(size.Z) / 2;
        double hx = Math.Abs(rx) * sx + Math.Abs(up.X) * sy + Math.Abs(bx) * sz;
        double hy = Math.Abs(ry) * sx + Math.Abs(up.Y) * sy + Math.Abs(by) * sz;
        double hz = Math.Abs(rz) * sx + Math.Abs(up.Z) * sy + Math.Abs(bz) * sz;
        bool tilted = Math.Abs(ry) > 0.05 && Math.Abs(ry) < 0.95 || Math.Abs(up.Y) > 0.05 && Math.Abs(up.Y) < 0.95;
        return new(pos.X, pos.Z, hx, hz, 0, -1, pos.Y, hy, tilted);
    }

    public static NavRect FromPart(Settings.Vec3 pos, Settings.Vec3 size, double lookX = 0, double lookZ = -1, double lookY = 0)
    {
        double l = Math.Sqrt(lookX * lookX + lookZ * lookZ);
        bool tilted = Math.Abs(lookY) > 0.05;
        if (l < 1e-6) { lookX = 0; lookZ = -1; l = 1; }
        return new(pos.X, pos.Z, Math.Abs(size.X) / 2, Math.Abs(size.Z) / 2, lookX / l, lookZ / l, pos.Y, Math.Abs(size.Y) / 2, tilted);
    }

    /// <summary>Blocks a body whose feet are at <paramref name="footY"/>: upright (not sloped/ramp), top above step height, bottom below the head.</summary>
    public bool BlocksBodyAt(double footY) => !Tilted && !NoBlock && Y + HalfY > footY + 2.5 && Y - HalfY < footY + 5;

    /// <summary>v0.2.47: floor walkable by feet at <paramref name="footY"/> (part top between 4 studs below the feet and the step limit).</summary>
    public bool IsFloorAt(double footY) => Y + HalfY <= footY + 2.5 && Y + HalfY >= footY - 4;

    /// <summary>Point inside the rectangle grown by <paramref name="grow"/> studs on each side.</summary>
    public bool Contains(double px, double pz, double grow = 0)
    {
        double dx = px - X, dz = pz - Z;
        double lx = dx * -LookZ + dz * LookX;   // projection onto RightVector (-lz, lx)
        double lz = dx * LookX + dz * LookZ;    // projection onto LookVector
        return Math.Abs(lx) <= HalfX + grow && Math.Abs(lz) <= HalfZ + grow;
    }

    /// <summary>Axis-aligned box containing the rectangle (+grow).</summary>
    public (double MinX, double MinZ, double MaxX, double MaxZ) Bounds(double grow = 0)
    {
        double ex = Math.Abs(LookZ) * HalfX + Math.Abs(LookX) * HalfZ + grow;
        double ez = Math.Abs(LookX) * HalfX + Math.Abs(LookZ) * HalfZ + grow;
        return (X - ex, Z - ez, X + ex, Z + ez);
    }
}

/// <summary>
/// Round navigation map (READ-ONLY, snapshot S15/ER2 24 Oct 2026):
/// Walkable = Workspace.World.Areas.&lt;n&gt; boxes (play zone); Obstacles = obstacle footprints in Workspace.World.Navigation
/// (Hxh: flat Parts at Y=3 named after props/buildings) + Barriers/NpcBarriers; Nodes = "navN" 4×4×4 waypoints (Evil Reflection: stairs).
/// </summary>
/// <remarks>Solids = CanCollide Parts in Workspace.World.Map (e.g. the see-through AlleyBarrier), filtered by body height during pathfinding.</remarks>
public sealed record NavMap(IReadOnlyList<NavRect> Walkable, IReadOnlyList<NavRect> Obstacles, IReadOnlyList<Settings.Vec3> Nodes, IReadOnlyList<NavRect>? Solids = null)
{
    /// <summary>v0.2.74: World.Map.Border*.Blocker diagnostics: "Border4 solid rubble (x,z)" etc. Logged when the path is not connected.</summary>
    public string? BorderDiag { get; init; }
    public bool IsEmpty => Walkable.Count == 0 && Obstacles.Count == 0 && Nodes.Count == 0 && (Solids?.Count ?? 0) == 0;
}

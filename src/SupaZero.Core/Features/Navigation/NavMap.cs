namespace SupaZero.Core.Game;

/// <summary>
/// Oriented rectangle in the XZ plane (Part: position, Size X/Z, yaw from LookVector). Size.Z along LookVector,
/// Size.X along RightVector (= Look × Up).
/// </summary>
public sealed record NavRect(double X, double Z, double HalfX, double HalfZ, double LookX, double LookZ, double Y, double HalfY = 0, bool Tilted = false)
{
    public static NavRect FromPart(Settings.Vec3 pos, Settings.Vec3 size, double lookX = 0, double lookZ = -1, double lookY = 0)
    {
        double l = Math.Sqrt(lookX * lookX + lookZ * lookZ);
        bool tilted = Math.Abs(lookY) > 0.05;
        if (l < 1e-6) { lookX = 0; lookZ = -1; l = 1; }
        return new(pos.X, pos.Z, Math.Abs(size.X) / 2, Math.Abs(size.Z) / 2, lookX / l, lookZ / l, pos.Y, Math.Abs(size.Y) / 2, tilted);
    }

    /// <summary>Blocks a body whose feet are at <paramref name="footY"/>: upright (not sloped/ramp), top above step height, bottom below the head.</summary>
    public bool BlocksBodyAt(double footY) => !Tilted && Y + HalfY > footY + 2.5 && Y - HalfY < footY + 5;

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

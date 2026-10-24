namespace SupaZero.Core.Game;

/// <summary>
/// Persegi berorientasi di bidang XZ (Part: posisi, Size X/Z, yaw dari LookVector). Size.Z searah LookVector,
/// Size.X searah RightVector (= Look × Up).
/// </summary>
public sealed record NavRect(double X, double Z, double HalfX, double HalfZ, double LookX, double LookZ, double Y)
{
    public static NavRect FromPart(Settings.Vec3 pos, Settings.Vec3 size, double lookX = 0, double lookZ = -1)
    {
        double l = Math.Sqrt(lookX * lookX + lookZ * lookZ);
        if (l < 1e-6) { lookX = 0; lookZ = -1; l = 1; }
        return new(pos.X, pos.Z, Math.Abs(size.X) / 2, Math.Abs(size.Z) / 2, lookX / l, lookZ / l, pos.Y);
    }

    /// <summary>Titik di dalam persegi yang diperbesar <paramref name="grow"/> stud tiap sisi.</summary>
    public bool Contains(double px, double pz, double grow = 0)
    {
        double dx = px - X, dz = pz - Z;
        double lx = dx * -LookZ + dz * LookX;   // proyeksi ke RightVector (-lz, lx)
        double lz = dx * LookX + dz * LookZ;    // proyeksi ke LookVector
        return Math.Abs(lx) <= HalfX + grow && Math.Abs(lz) <= HalfZ + grow;
    }

    /// <summary>Kotak sejajar sumbu yang memuat persegi (+grow).</summary>
    public (double MinX, double MinZ, double MaxX, double MaxZ) Bounds(double grow = 0)
    {
        double ex = Math.Abs(LookZ) * HalfX + Math.Abs(LookX) * HalfZ + grow;
        double ez = Math.Abs(LookX) * HalfX + Math.Abs(LookZ) * HalfZ + grow;
        return (X - ex, Z - ez, X + ex, Z + ez);
    }
}

/// <summary>
/// Peta navigasi ronde (READ-ONLY, snapshot S15/ER2 24 Okt 2026):
/// Walkable = kotak Workspace.World.Areas.&lt;n&gt; (wilayah main); Obstacles = jejak rintangan di Workspace.World.Navigation
/// (Hxh: Part datar Y=3 bernama prop/bangunan) + Barriers/NpcBarriers; Nodes = titik jalan "navN" 4×4×4 (Evil Reflection: tangga).
/// </summary>
public sealed record NavMap(IReadOnlyList<NavRect> Walkable, IReadOnlyList<NavRect> Obstacles, IReadOnlyList<Settings.Vec3> Nodes)
{
    public bool IsEmpty => Walkable.Count == 0 && Obstacles.Count == 0 && Nodes.Count == 0;
}

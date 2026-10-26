using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>
/// Pathfinding in the XZ plane with A* on a grid built from <see cref="NavMap"/> (no PathfindingService, purely from memory that was read).
/// Walkable cell = inside an Areas box (slightly grown) or near a "navN" node; blocked cell = inside an obstacle footprint
/// (grown by the character body width). The path is smoothed (string pulling) into a few waypoints.
/// </summary>
public sealed class PathPlanner
{
    /// <summary>Areas box margin, nav node radius, body width (obstacle inflation), in studs.</summary>
    public const double AreaGrow = 1, NodeRadius = 5, BodyInflate = 1.6, EndpointRadius = 4;
    /// <summary>
    /// v0.2.47: step cost on floors outside the Areas boxes. The official inter-area route can leave the Areas boxes
    /// (Hxh Area 4 → 5: around the block via the east road x≈598, then west at z≈-56), so solid floor outside Areas
    /// is still walkable, but only when there is no full path inside the Areas boxes (second pass).
    /// </summary>
    public const double OutsideCost = 1.5;
    private const int MaxCells = 300_000;

    private NavMap? _map;
    private double _minX, _minZ, _cell;
    private int _w, _h;
    private bool[] _free = [];
    private bool[] _outside = [];
    private bool _allowOutside;
    private bool Free(int i) => _free[i] && (_allowOutside || !_outside[i]);

    /// <summary>Waypoints from <paramref name="from"/> to <paramref name="to"/> (excluding the start point), or null when there is no path.</summary>
    public List<Vec3>? Find(NavMap map, Vec3 from, Vec3 to)
    {
        if (NeedBuild(map, from, to)) Build(map, from, to);
        _allowOutside = false;
        var first = FindPass(from, to);
        if (first is not null && !Partial || !_outside.Any(o => o)) { UsedOutside = false; return first; }
        // Second pass: may use floor outside the Areas boxes (detour around the block).
        string? firstFail = LastFailure; bool firstPartial = Partial;
        _allowOutside = true;
        var second = FindPass(from, to);
        _allowOutside = false;
        if (second is not null && !Partial) return second;
        LastFailure = firstFail; Partial = firstPartial; UsedOutside = false;
        return first;
    }

    private List<Vec3>? FindPass(Vec3 from, Vec3 to)
    {
        UsedOutside = false; Partial = false;
        var s = Nearest(Cell(from)); var g = Nearest(Cell(to), Cell(from), 30);
        LastFailure = s < 0 ? $"character position ({from.X:0},{from.Z:0}) blocked by obstacles"
            : g < 0 ? $"destination ({to.X:0},{to.Z:0}) blocked by obstacles" : null;
        if (s < 0) return null;
        if (g < 0) g = Cell(to);
        bool goalShifted = g != Cell(to);
        var cells = AStar(s, g, out bool reached);
        if (cells is null) { LastFailure ??= "no reachable cell"; return null; }
        Partial = !reached;
        if (!reached) LastFailure ??= $"destination ({to.X:0},{to.Z:0}) not connected (door/Blocker closed?), walking to the nearest point";
        UsedOutside = _allowOutside && cells.Any(c => _outside[c]);
        var pts = Smooth(cells);
        var result = pts.Skip(1).Select(c => new Vec3(_minX + (c % _w + 0.5) * _cell, to.Y, _minZ + (c / _w + 0.5) * _cell)).ToList();
        if (reached && !goalShifted) { if (result.Count > 0) result[^1] = to; else result.Add(to); }
        if (reached && goalShifted) LastFailure = $"destination ({to.X:0},{to.Z:0}) inside an obstacle, stopping at the nearest side";
        return result.Count > 0 ? result : null;
    }

    /// <summary>Whether the straight line from a to b is obstacle-free (grid already built for this map).</summary>
    public bool Clear(NavMap map, Vec3 a, Vec3 b)
    {
        if (NeedBuild(map, a, b)) Build(map, a, b);
        int ca = Cell(a), cb = Cell(b);
        _allowOutside = false;
        return ca >= 0 && cb >= 0 && Line(ca, cb);
    }

    private double _builtY;
    /// <summary>Last reason the path failed / was only partial (for the log).</summary>
    public string? LastFailure { get; private set; }
    /// <summary>The last path stops at the nearest reachable point, not the destination.</summary>
    public bool Partial { get; private set; }
    private readonly List<(double X, double Z, double R)> _blocks = [];

    /// <summary>Mark the point where the character got stuck (an object not on the map) as an obstacle for this map.</summary>
    public void Block(double x, double z, double r = 2.5)
    {
        if (_blocks.Count >= 30) _blocks.RemoveAt(0);
        _blocks.Add((x, z, r));
        if (_w > 0) Paint((x - r, z - r, x + r, z + r), (px, pz) => Navigator.Distance2D(px, pz, x, z) <= r, false);
    }

    /// <summary>v0.3.9: clear stuck marks (they can close the only gap), then rebuild the grid on next use.</summary>
    public bool ClearBlocks()
    {
        if (_blocks.Count == 0) return false;
        _blocks.Clear();
        _map = null;
        return true;
    }

    private bool NeedBuild(NavMap map, Vec3 a, Vec3 b) =>
        !ReferenceEquals(map, _map) || !Inside(a) || !Inside(b) || Math.Abs(FootY(map, a) - _builtY) > 3;

    /// <summary>
    /// v0.2.64: foot height = top of the highest floor directly below the character, not HRP.Y − 3. While jumping/dashing (HRP Y ≥ 8)
    /// the Hxh/Shadow Gate east road no longer counted as floor → partial path into a dead-end alley (Byzan log 24 Oct 2026 20:54).
    /// </summary>
    public static double FootY(NavMap map, Vec3 from)
    {
        double best = double.NegativeInfinity;
        if (map.Solids is { } solids)
            foreach (var r in solids)
            {
                if (r.Tilted || !r.Contains(from.X, from.Z)) continue;
                double top = r.Y + r.HalfY;
                if (top <= from.Y - 1 && top >= from.Y - 30 && top > best) best = top;
            }
        return double.IsNegativeInfinity(best) ? from.Y - 3 : best;
    }

    private bool Inside(Vec3 p) => _w > 0 && p.X >= _minX && p.Z >= _minZ && p.X < _minX + _w * _cell && p.Z < _minZ + _h * _cell;
    private int Cell(Vec3 p)
    {
        int x = (int)Math.Floor((p.X - _minX) / _cell), z = (int)Math.Floor((p.Z - _minZ) / _cell);
        return x < 0 || z < 0 || x >= _w || z >= _h ? -1 : z * _w + x;
    }

    private void Build(NavMap map, Vec3 from, Vec3 to)
    {
        if (!ReferenceEquals(map, _map)) _blocks.Clear();
        _map = map;
        _builtY = FootY(map, from);
        double minX = Math.Min(from.X, to.X), minZ = Math.Min(from.Z, to.Z), maxX = Math.Max(from.X, to.X), maxZ = Math.Max(from.Z, to.Z);
        foreach (var r in map.Walkable) { var b = r.Bounds(AreaGrow); minX = Math.Min(minX, b.MinX); minZ = Math.Min(minZ, b.MinZ); maxX = Math.Max(maxX, b.MaxX); maxZ = Math.Max(maxZ, b.MaxZ); }
        foreach (var n in map.Nodes) { minX = Math.Min(minX, n.X - NodeRadius); minZ = Math.Min(minZ, n.Z - NodeRadius); maxX = Math.Max(maxX, n.X + NodeRadius); maxZ = Math.Max(maxZ, n.Z + NodeRadius); }
        // Very large area (e.g. Areas.1 682 studs) → cells are enlarged so the grid stays ≤ MaxCells.
        minX -= 20; minZ -= 20; maxX += 20; maxZ += 20;
        _cell = 2;
        while ((maxX - minX) / _cell * ((maxZ - minZ) / _cell) > MaxCells) _cell += 0.5;
        _minX = minX; _minZ = minZ;
        _w = Math.Max(1, (int)Math.Ceiling((maxX - minX) / _cell));
        _h = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / _cell));
        _free = new bool[_w * _h];
        _outside = new bool[_w * _h];
        bool open = map.Walkable.Count == 0 && map.Nodes.Count == 0;
        if (open) Array.Fill(_free, true);
        foreach (var r in map.Walkable) Paint(r.Bounds(AreaGrow), (x, z) => r.Contains(x, z, AreaGrow), true);
        foreach (var n in map.Nodes) Paint((n.X - NodeRadius, n.Z - NodeRadius, n.X + NodeRadius, n.Z + NodeRadius), (x, z) => Navigator.Distance2D(x, z, n.X, n.Z) <= NodeRadius, true);
        double footY = _builtY;
        // v0.2.47: solid floor (Baseplate, road platforms) outside the Areas boxes = walkable at a higher cost (not a chasm/void).
        if (!open && map.Solids is { } floors)
            foreach (var r in floors)
                if (r.IsFloorAt(footY) && r.HalfX >= 2 && r.HalfZ >= 2)
                    PaintOutside(r);
        // Start/destination outside the walkable zone (e.g. raid spawn 56 studs before the stair node): connect with a straight corridor to the nearest walkable cell.
        if (!open) foreach (var p in new[] { from, to }) Connect(p);
        foreach (var p in new[] { from, to }) Paint((p.X - EndpointRadius, p.Z - EndpointRadius, p.X + EndpointRadius, p.Z + EndpointRadius), (x, z) => Navigator.Distance2D(x, z, p.X, p.Z) <= EndpointRadius, true);
        foreach (var r in map.Obstacles) Paint(r.Bounds(BodyInflate), (x, z) => r.Contains(x, z, BodyInflate), false);
        // Solid Parts in Map at body height (HumanoidRootPart ≈ 3 studs above the feet); floors, ramps and roofs are ignored.
        if (map.Solids is { } solids)
            foreach (var r in solids)
                // v0.2.56: a destination inside a building (the "AREA 5" marker on the Hxh facade) no longer makes that building passable;
                // the destination is moved to the nearest free cell (Nearest). Only the part the player is standing on is excluded.
                if (r.BlocksBodyAt(footY) && !r.Contains(from.X, from.Z))
                    Paint(r.Bounds(BodyInflate), (x, z) => r.Contains(x, z, BodyInflate), false);
        foreach (var (bx, bz, br) in _blocks) Paint((bx - br, bz - br, bx + br, bz + br), (x, z) => Navigator.Distance2D(x, z, bx, bz) <= br, false);
    }

    private void PaintOutside(NavRect r)
    {
        var b = r.Bounds();
        int x0 = Math.Max(0, (int)Math.Floor((b.MinX - _minX) / _cell)), x1 = Math.Min(_w - 1, (int)Math.Floor((b.MaxX - _minX) / _cell));
        int z0 = Math.Max(0, (int)Math.Floor((b.MinZ - _minZ) / _cell)), z1 = Math.Min(_h - 1, (int)Math.Floor((b.MaxZ - _minZ) / _cell));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = z * _w + x;
                if (_free[i] || !r.Contains(_minX + (x + 0.5) * _cell, _minZ + (z + 0.5) * _cell)) continue;
                _free[i] = true;
                _outside[i] = true;
            }
    }

    /// <summary>The last path used floor outside the Areas boxes (for the log).</summary>
    public bool UsedOutside { get; private set; }

    private void Connect(Vec3 p)
    {
        int c = Cell(p);
        if (c < 0 || _free[c]) return;
        int px = c % _w, pz = c / _w, best = -1;
        long bestD = long.MaxValue;
        for (int i = 0; i < _free.Length; i++)
        {
            if (!_free[i]) continue;
            long dx = i % _w - px, dz = i / _w - pz, d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0) return;
        double bx = best % _w, bz = best / _w;
        int steps = (int)Math.Ceiling(Math.Sqrt(bestD) * 2) + 1;
        for (int k = 0; k <= steps; k++)
        {
            double t = (double)k / steps, x = px + (bx - px) * t, z = pz + (bz - pz) * t;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = (int)Math.Round(x) + dx, cz = (int)Math.Round(z) + dz;
                    if (cx >= 0 && cz >= 0 && cx < _w && cz < _h) _free[cz * _w + cx] = true;
                }
        }
    }

    private void Paint((double MinX, double MinZ, double MaxX, double MaxZ) b, Func<double, double, bool> test, bool value)
    {
        int x0 = Math.Max(0, (int)Math.Floor((b.MinX - _minX) / _cell)), x1 = Math.Min(_w - 1, (int)Math.Floor((b.MaxX - _minX) / _cell));
        int z0 = Math.Max(0, (int)Math.Floor((b.MinZ - _minZ) / _cell)), z1 = Math.Min(_h - 1, (int)Math.Floor((b.MaxZ - _minZ) / _cell));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                if (test(_minX + (x + 0.5) * _cell, _minZ + (z + 0.5) * _cell)) _free[z * _w + x] = value;
    }

    /// <summary>Nearest free cell (the character can stand at the edge of an inflated obstacle).</summary>
    private int Nearest(int c, int prefer = -1, int maxR = 12)
    {
        if (c < 0) return -1;
        if (Free(c)) return c;
        int cx = c % _w, cz = c / _w;
        int px = prefer >= 0 ? prefer % _w : cx, pz = prefer >= 0 ? prefer / _w : cz;
        for (int r = 1; r <= maxR; r++)
        {
            int best = -1; long bestD = long.MaxValue;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= _w || z >= _h || !Free(z * _w + x)) continue;
                    // Among several equally distant free cells, pick the one closest to the player (the building side facing the player).
                    long d = (long)(x - px) * (x - px) + (long)(z - pz) * (z - pz);
                    if (d < bestD) { bestD = d; best = z * _w + x; }
                }
            if (best >= 0) return best;
        }
        return -1;
    }

    private List<int>? AStar(int s, int g, out bool reached)
    {
        reached = true;
        if (s == g) return [s, g];
        int best = s; double bestH = double.MaxValue;
        var gScore = new Dictionary<int, double> { [s] = 0 };
        var came = new Dictionary<int, int>();
        var open = new PriorityQueue<int, double>();
        int gx = g % _w, gz = g / _w;
        double H(int c) { int dx = Math.Abs(c % _w - gx), dz = Math.Abs(c / _w - gz); return Math.Max(dx, dz) + 0.4142 * Math.Min(dx, dz); }
        bestH = H(s);
        open.Enqueue(s, H(s));
        var closed = new HashSet<int>();
        int guard = 0;
        while (open.TryDequeue(out int c, out _))
        {
            if (c == g)
            {
                var path = new List<int> { c };
                while (came.TryGetValue(c, out int p)) { c = p; path.Add(c); }
                path.Reverse();
                return path;
            }
            if (!closed.Add(c) || ++guard > MaxCells) continue;
            double hc = H(c);
            if (hc < bestH) { bestH = hc; best = c; }
            int cx = c % _w, cz = c / _w;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= _w || z >= _h) continue;
                    int n = z * _w + x;
                    if (!Free(n) || closed.Contains(n)) continue;
                    // Diagonals must not cut obstacle corners.
                    if (dx != 0 && dz != 0 && (!Free(cz * _w + x) || !Free(z * _w + cx))) continue;
                    double ng = gScore[c] + (dx != 0 && dz != 0 ? 1.4142 : 1) * (_outside[n] ? OutsideCost : 1);
                    if (gScore.TryGetValue(n, out double old) && old <= ng) continue;
                    gScore[n] = ng;
                    came[n] = c;
                    open.Enqueue(n, ng + H(n));
                }
        }
        // Destination unreachable: path to the reachable cell closest to the destination (if it is actually closer than the start position).
        reached = false;
        if (best == s || H(s) - bestH < 3) return null;
        var part = new List<int> { best };
        int cur = best;
        while (came.TryGetValue(cur, out int p)) { cur = p; part.Add(cur); }
        part.Reverse();
        return part;
    }

    /// <summary>String pulling: skip cells as long as the straight line stays clear.</summary>
    private List<int> Smooth(List<int> cells)
    {
        var outp = new List<int> { cells[0] };
        int i = 0;
        while (i < cells.Count - 1)
        {
            int j = cells.Count - 1;
            while (j > i + 1 && !Line(cells[i], cells[j])) j--;
            outp.Add(cells[j]);
            i = j;
        }
        return outp;
    }

    /// <summary>Cell line a→b is clear (sampling every ¼ cell, including neighbors so it does not graze corners).</summary>
    private bool Line(int a, int b)
    {
        double ax = a % _w + 0.5, az = a / _w + 0.5, bx = b % _w + 0.5, bz = b / _w + 0.5;
        double len = Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az));
        int steps = Math.Max(1, (int)Math.Ceiling(len * 4));
        for (int k = 0; k <= steps; k++)
        {
            double t = (double)k / steps;
            int x = (int)(ax + (bx - ax) * t), z = (int)(az + (bz - az) * t);
            if (x < 0 || z < 0 || x >= _w || z >= _h || !Free(z * _w + x)) return false;
        }
        return true;
    }
}

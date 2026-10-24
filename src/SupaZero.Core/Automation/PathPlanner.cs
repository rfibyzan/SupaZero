using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>
/// Cari jalur di bidang XZ dengan A* di grid dari <see cref="NavMap"/> (tanpa PathfindingService, murni dari memori yang dibaca).
/// Sel jalan = di dalam kotak Areas (diperbesar sedikit) atau dekat node "navN"; sel tertutup = di dalam jejak rintangan
/// (diperbesar setebal badan karakter). Jalur dirapikan (string pulling) jadi beberapa titik belok.
/// </summary>
public sealed class PathPlanner
{
    /// <summary>Margin kotak Areas, jari-jari node nav, tebal badan (inflasi rintangan), stud.</summary>
    public const double AreaGrow = 1, NodeRadius = 5, BodyInflate = 1.6, EndpointRadius = 4;
    /// <summary>
    /// v0.2.47: biaya langkah di lantai di luar kotak Areas. Rute resmi antar-area bisa keluar kotak Areas
    /// (Hxh Area 4 → 5: memutar blok lewat jalan timur x≈598 lalu ke barat di z≈-56), jadi lantai padat di luar Areas
    /// tetap bisa dilewati, tapi hanya bila tidak ada jalur penuh di dalam kotak Areas (pass kedua).
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

    /// <summary>Titik belok dari <paramref name="from"/> ke <paramref name="to"/> (tidak termasuk titik awal), atau null bila tidak ada jalur.</summary>
    public List<Vec3>? Find(NavMap map, Vec3 from, Vec3 to)
    {
        if (NeedBuild(map, from, to)) Build(map, from, to);
        _allowOutside = false;
        var first = FindPass(from, to);
        if (first is not null && !Partial || !_outside.Any(o => o)) { UsedOutside = false; return first; }
        // Pass kedua: boleh lewat lantai di luar kotak Areas (jalan memutar blok).
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
        var s = Nearest(Cell(from)); var g = Nearest(Cell(to));
        LastFailure = s < 0 ? $"posisi karakter ({from.X:0},{from.Z:0}) tertutup rintangan"
            : g < 0 ? $"tujuan ({to.X:0},{to.Z:0}) tertutup rintangan" : null;
        if (s < 0) return null;
        if (g < 0) g = Cell(to);
        var cells = AStar(s, g, out bool reached);
        if (cells is null) { LastFailure ??= "tidak ada sel yang bisa didekati"; return null; }
        Partial = !reached;
        if (!reached) LastFailure ??= $"tujuan ({to.X:0},{to.Z:0}) tidak tersambung (pintu/Blocker tertutup?), jalan ke titik terdekat";
        UsedOutside = _allowOutside && cells.Any(c => _outside[c]);
        var pts = Smooth(cells);
        var result = pts.Skip(1).Select(c => new Vec3(_minX + (c % _w + 0.5) * _cell, to.Y, _minZ + (c / _w + 0.5) * _cell)).ToList();
        if (reached) { if (result.Count > 0) result[^1] = to; else result.Add(to); }
        return result.Count > 0 ? result : null;
    }

    /// <summary>Apakah garis lurus dari a ke b bebas rintangan (grid sudah dibangun untuk map ini).</summary>
    public bool Clear(NavMap map, Vec3 a, Vec3 b)
    {
        if (NeedBuild(map, a, b)) Build(map, a, b);
        int ca = Cell(a), cb = Cell(b);
        _allowOutside = false;
        return ca >= 0 && cb >= 0 && Line(ca, cb);
    }

    private double _builtY;
    /// <summary>Alasan terakhir jalur gagal / hanya parsial (untuk log).</summary>
    public string? LastFailure { get; private set; }
    /// <summary>Jalur terakhir berhenti di titik terdekat yang terjangkau, bukan tujuan.</summary>
    public bool Partial { get; private set; }
    private readonly List<(double X, double Z, double R)> _blocks = [];

    /// <summary>Tandai titik tempat karakter tersangkut (objek yang tidak ada di peta) sebagai rintangan untuk map ini.</summary>
    public void Block(double x, double z, double r = 2.5)
    {
        if (_blocks.Count >= 30) _blocks.RemoveAt(0);
        _blocks.Add((x, z, r));
        if (_w > 0) Paint((x - r, z - r, x + r, z + r), (px, pz) => Navigator.Distance2D(px, pz, x, z) <= r, false);
    }

    private bool NeedBuild(NavMap map, Vec3 a, Vec3 b) =>
        !ReferenceEquals(map, _map) || !Inside(a) || !Inside(b) || Math.Abs(a.Y - _builtY) > 6;

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
        _builtY = from.Y;
        double minX = Math.Min(from.X, to.X), minZ = Math.Min(from.Z, to.Z), maxX = Math.Max(from.X, to.X), maxZ = Math.Max(from.Z, to.Z);
        foreach (var r in map.Walkable) { var b = r.Bounds(AreaGrow); minX = Math.Min(minX, b.MinX); minZ = Math.Min(minZ, b.MinZ); maxX = Math.Max(maxX, b.MaxX); maxZ = Math.Max(maxZ, b.MaxZ); }
        foreach (var n in map.Nodes) { minX = Math.Min(minX, n.X - NodeRadius); minZ = Math.Min(minZ, n.Z - NodeRadius); maxX = Math.Max(maxX, n.X + NodeRadius); maxZ = Math.Max(maxZ, n.Z + NodeRadius); }
        // Area sangat luas (mis. Areas.1 682 stud) → sel diperbesar agar grid tetap ≤ MaxCells.
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
        double footY = from.Y - 3;
        // v0.2.47: lantai padat (Baseplate, platform jalan) di luar kotak Areas = boleh dilewati dengan biaya lebih (bukan jurang/void).
        if (!open && map.Solids is { } floors)
            foreach (var r in floors)
                if (r.IsFloorAt(footY) && r.HalfX >= 2 && r.HalfZ >= 2)
                    PaintOutside(r);
        // Titik awal/tujuan di luar wilayah jalan (mis. spawn raid 56 stud sebelum node tangga): sambungkan dengan koridor lurus ke sel jalan terdekat.
        if (!open) foreach (var p in new[] { from, to }) Connect(p);
        foreach (var p in new[] { from, to }) Paint((p.X - EndpointRadius, p.Z - EndpointRadius, p.X + EndpointRadius, p.Z + EndpointRadius), (x, z) => Navigator.Distance2D(x, z, p.X, p.Z) <= EndpointRadius, true);
        foreach (var r in map.Obstacles) Paint(r.Bounds(BodyInflate), (x, z) => r.Contains(x, z, BodyInflate), false);
        // Part padat di Map setinggi badan (HumanoidRootPart ≈ 3 stud di atas kaki); lantai, ramp, dan atap dilewati.
        if (map.Solids is { } solids)
            foreach (var r in solids)
                if (r.BlocksBodyAt(footY) && !r.Contains(from.X, from.Z) && !r.Contains(to.X, to.Z))
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

    /// <summary>Jalur terakhir lewat lantai di luar kotak Areas (untuk log).</summary>
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

    /// <summary>Sel bebas terdekat (karakter bisa berdiri di tepi rintangan yang diinflasi).</summary>
    private int Nearest(int c)
    {
        if (c < 0) return -1;
        if (Free(c)) return c;
        int cx = c % _w, cz = c / _w;
        for (int r = 1; r <= 12; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x >= 0 && z >= 0 && x < _w && z < _h && Free(z * _w + x)) return z * _w + x;
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
                    // Diagonal tidak boleh memotong sudut rintangan.
                    if (dx != 0 && dz != 0 && (!Free(cz * _w + x) || !Free(z * _w + cx))) continue;
                    double ng = gScore[c] + (dx != 0 && dz != 0 ? 1.4142 : 1) * (_outside[n] ? OutsideCost : 1);
                    if (gScore.TryGetValue(n, out double old) && old <= ng) continue;
                    gScore[n] = ng;
                    came[n] = c;
                    open.Enqueue(n, ng + H(n));
                }
        }
        // Tujuan tidak terjangkau: jalur ke sel terjangkau paling dekat ke tujuan (bila memang lebih dekat dari posisi awal).
        reached = false;
        if (best == s || H(s) - bestH < 3) return null;
        var part = new List<int> { best };
        int cur = best;
        while (came.TryGetValue(cur, out int p)) { cur = p; part.Add(cur); }
        part.Reverse();
        return part;
    }

    /// <summary>String pulling: lompati sel selama garis lurus tetap bebas.</summary>
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

    /// <summary>Garis sel a→b bebas (sampling ¼ sel, termasuk tetangga agar tidak menyerempet sudut).</summary>
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

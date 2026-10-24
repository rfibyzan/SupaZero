namespace SupaZero.Core.Game;

/// <summary>Pembacaan peta navigasi (Areas, Navigation, Barriers) dengan cache per map. READ-ONLY.</summary>
public sealed partial class GameReader
{
    private (long Key, int Count, NavMap Map)? _navCache;

    private NavMap? ReadNav(long ws)
    {
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        if (world == 0) return null;
        long areas = reader.FindChild(world, AnimeZeroPaths.WorldAreas);
        long nav = reader.FindChild(world, AnimeZeroPaths.WorldNavigation);
        long key = areas ^ (nav << 1);
        var areaKids = areas != 0 ? reader.GetChildren(areas, 64) : [];
        var navKids = nav != 0 ? reader.GetChildren(nav, 4096) : [];
        int count = areaKids.Count * 100_000 + navKids.Count;
        if (_navCache is { } c && c.Key == key && c.Count == count) return c.Map;

        var walk = new List<NavRect>();
        var obstacles = new List<NavRect>();
        var nodes = new List<Settings.Vec3>();
        foreach (long a in areaKids)
            if (ReadRect(a) is { } r) walk.Add(r);
        foreach (long n in navKids)
        {
            if (!props.TryGetWorldPosition(n, out float x, out float y, out float z)) continue;
            var size = ParseVec(Prop(n, "Size"));
            string name = reader.GetName(n);
            if (name.StartsWith("nav", StringComparison.OrdinalIgnoreCase) && size is { X: <= 8, Z: <= 8 })
                nodes.Add(new(x, y, z));
            else if (ReadRect(n) is { } r) obstacles.Add(r);
        }
        foreach (string folder in AnimeZeroPaths.WorldBarrierFolders)
        {
            long f = reader.FindChild(world, folder);
            if (f == 0) continue;
            foreach (long b in reader.GetChildren(f, 512))
                if (ReadRect(b) is { } r) obstacles.Add(r);
        }
        var map = new NavMap(walk, obstacles, nodes);
        _navCache = (key, count, map);
        return map;
    }

    private NavRect? ReadRect(long part)
    {
        if (!props.TryGetWorldPosition(part, out float x, out float y, out float z)) return null;
        var size = ParseVec(Prop(part, "Size"));
        if (size is null || size.X <= 0 || size.Z <= 0) return null;
        var look = ParseVec(Prop(part, "LookVector"));
        return NavRect.FromPart(new(x, y, z), size, look?.X ?? 0, look?.Z ?? -1);
    }
}

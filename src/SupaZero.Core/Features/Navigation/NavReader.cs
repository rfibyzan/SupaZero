namespace SupaZero.Core.Game;

/// <summary>Reads the navigation map (Areas, Navigation, Barriers) with a per-map cache. READ-ONLY.</summary>
public sealed partial class GameReader
{
    private (long Key, int Count, NavMap Map)? _navCache;
    private long _navCheckedAt;

    private NavMap? ReadNav(long ws)
    {
        // Check for map changes (the game removes inter-area Border/Blocker when an area is cleared) at most every 2 s.
        long nowMs = Environment.TickCount64;
        if (_navCache is { } fast && nowMs - _navCheckedAt < 2000) return fast.Map;
        _navCheckedAt = nowMs;
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        if (world == 0) return null;
        long areas = reader.FindChild(world, AnimeZeroPaths.WorldAreas);
        long nav = reader.FindChild(world, AnimeZeroPaths.WorldNavigation);
        long key = areas ^ (nav << 1);
        var areaKids = areas != 0 ? reader.GetChildren(areas, 64) : [];
        var navKids = nav != 0 ? reader.GetChildren(nav, 4096) : [];
        long mapF0 = reader.FindChild(world, AnimeZeroPaths.WorldMap);
        var mapChildren = mapF0 != 0 ? reader.GetChildren(mapF0, 4096) : [];
        int mapKids = mapChildren.Count;
        key ^= mapF0 << 2;
        int count = areaKids.Count * 100_000_000 + mapKids * 10_000 + navKids.Count;
        // v0.2.69: rubble destroyed → Border*.Blocker only changes CanCollide (Map child count unchanged), so the old cache still
        // treats it as a wall → path to the next area "not connected" and the character gets stuck (Byzan log 24 Oct 22:31, Area 5).
        long blockSig = 17;
        var borderDiag = new List<string>();
        foreach (long b in mapChildren)
        {
            string bn = reader.GetName(b);
            if (!bn.StartsWith("Border", StringComparison.Ordinal)) continue;
            long blk = reader.FindChild(b, "Blocker");
            var bp = blk != 0 ? props.Read(blk) : null;
            bool solid = bp is not null && bp.TryGetValue("CanCollide", out var bc) && bc == "true";
            // v0.3.9: Border*.Rubble (ClimbStop + rubble mesh, CanCollide) is removed by the game SEPARATELY from Blocker CanCollide→false
            // (snapshot 00:57:52 vs 01:02:03). Without this the cache can be built when the Blocker is already open but Rubble still exists →
            // ±23 rubble parts remain walls on the east road → "Path to area 5 not connected" (Byzan log 25 Oct 10:46).
            long rubNode = reader.FindChild(b, "Rubble");
            int rubKids = rubNode != 0 ? reader.GetChildren(rubNode, 256).Count : -1;
            blockSig = blockSig * 31 + b + (blk == 0 ? 0 : solid ? 2 : 1);
            blockSig = blockSig * 31 + rubNode + rubKids;
            string where = bp is not null && ParseVec(bp.GetValueOrDefault("Position")) is { } bpos
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" ({bpos.X:0},{bpos.Z:0})") : "";
            bool rub = blk != 0 && reader.FindChild(blk, AnimeZeroPaths.RubbleBar) != 0;
            borderDiag.Add($"{bn} {(blk == 0 ? "no Blocker" : solid ? "solid" : "open")}{(rub ? " +RubbleBar" : "")}{where}");
        }
        key ^= blockSig << 3;
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
        // Walls/solid objects in Map (AlleyBarrier: see-through, CanCollide true). Floors/ramps are filtered during pathfinding.
        var solids = new List<NavRect>();
        long mapF = reader.FindChild(world, AnimeZeroPaths.WorldMap);
        if (mapF != 0) CollectSolids(mapF, 0, solids);
        var map = new NavMap(walk, obstacles, nodes, solids) { BorderDiag = borderDiag.Count > 0 ? string.Join("; ", borderDiag) : "no Border* in World.Map" };
        _navCache = (key, count, map);
        return map;
    }

    private void CollectSolids(long node, int depth, List<NavRect> list)
    {
        if (depth > 5 || list.Count >= 6000) return;
        foreach (long c in reader.GetChildren(node, 2048))
        {
            string cls = reader.GetClassName(c);
            if (cls is "Model" or "Folder") { CollectSolids(c, depth + 1, list); continue; }
            if (!cls.EndsWith("Part", StringComparison.Ordinal) && cls is not ("UnionOperation" or "MeshPart" or "WedgePart" or "TrussPart")) continue;
            var pr = props.Read(c);
            if (!pr.TryGetValue("CanCollide", out var cc) || cc != "true") continue;
            if (ReadRect(c, pr) is not { } r) continue;
            // v0.3.29 (Byzan 27 Oct 23:11, West City Act 1; RIE World 23:14:02): Map.MAP.DIRT_MOUND_1 is a low mound mesh whose bounding box
            // (195x16x263) covers the whole Area 2→3 road (the game's own route), and the DES_ROAD mesh under Spawns.3 has its top 3 studs
            // above the INT_ROAD feet → "Path to area 3: destination (-18,601) blocked". Mounds/road meshes outside Border*.Rubble never
            // block (they still count as floor); the ones inside Border*.Rubble stay solid (real rubble).
            if (IsWalkableMound(reader.GetName(c), reader.GetName(node))) r = r with { NoBlock = true };
            list.Add(r);
        }
    }

    // v0.3.34 (Byzan 27 Oct 23:33, West City "Enter area 5/5", log "Path to area 5: from (519,1022) to (711,1043), not found: no reachable
    // cell"; RIE World 23:14:02): MAP.ROCK_PATCH_A/B are scattered-rock patch meshes whose bounding boxes are 139-275 studs wide with tops
    // 17-24 studs while the road through them is clear (screenshot). Same treatment as DIRT_MOUND: floor, never a wall. A real rock the
    // character bumps into is still handled by the stuck marker (Block) + re-plan.
    // v0.3.44 (Byzan 28 Oct 00:52, Spirit's Bathtub floor 2 "Path to stairs not found"; RIE World 00:45:04): Map.S-bathhouse_STAIRS holds
    // the spiral stair mesh Cube.105 (67x73x72 bounding box over the whole hall) and Cube.107 (203x110x441) → the hall was one big wall.
    // Meshes directly under a *_STAIRS model are floor, never walls (the stair route itself is recorded in ElevatorRoutes).
    internal static bool IsWalkableMound(string name, string parentName) =>
        parentName.EndsWith("_STAIRS", StringComparison.OrdinalIgnoreCase) ||
        (name.StartsWith("DIRT_MOUND", StringComparison.Ordinal) || name.StartsWith("ROCK_PATCH", StringComparison.Ordinal) || name.EndsWith("_ROAD", StringComparison.Ordinal) || name.Contains("_ROAD_", StringComparison.Ordinal))
        && parentName != AnimeZeroPaths.RubbleName;

    private NavRect? ReadRect(long part, SortedDictionary<string, string>? pr = null)
    {
        if (pr is not null)
        {
            var pos = ParseVec(pr.GetValueOrDefault("Position"));
            var sz = ParseVec(pr.GetValueOrDefault("Size"));
            var lv = ParseVec(pr.GetValueOrDefault("LookVector"));
            return pos is null || sz is null || sz.X <= 0 || sz.Z <= 0 ? null : NavRect.FromPart(pos, sz, lv?.X ?? 0, lv?.Z ?? -1, lv?.Y ?? 0);
        }
        if (!props.TryGetWorldPosition(part, out float x, out float y, out float z)) return null;
        var size = ParseVec(Prop(part, "Size"));
        if (size is null || size.X <= 0 || size.Z <= 0) return null;
        var look = ParseVec(Prop(part, "LookVector"));
        return NavRect.FromPart(new(x, y, z), size, look?.X ?? 0, look?.Z ?? -1, look?.Y ?? 0);
    }
}

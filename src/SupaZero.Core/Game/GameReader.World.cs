using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    private List<EnemyInfo> ReadEnemies(long folder)
    {
        var list = new List<EnemyInfo>();
        if (folder == 0) return list;
        foreach (long m in reader.GetChildren(folder, 128))
        {
            long hrp = reader.FindChild(m, "HumanoidRootPart");
            if (hrp == 0 || !props.TryGetWorldPosition(hrp, out float x, out float y, out float z)) continue;
            double hp = 1, max = 1;
            long hum = reader.FindChild(m, "Humanoid");
            if (hum != 0)
            {
                var hp_ = props.Read(hum, "Humanoid");
                if (hp_.TryGetValue("Health", out var h)) hp = Num(h, 1);
                if (hp_.TryGetValue("MaxHealth", out var mh)) max = Num(mh, 1);
            }
            int hl = 0;
            foreach (long ch in reader.GetChildren(m, 64))
                if (reader.GetName(ch) == "Highlight" && reader.GetClassName(ch) == "Highlight") hl++;
            list.Add(new EnemyInfo(reader.GetName(m), new(x, y, z), hp, max, hl));
        }
        return list;
    }

    /// <summary>Active items in Workspace.PooledDrops (cash/health). Parked ones (Y = -5000) are skipped.</summary>
    private List<PickupInfo> ReadPickups(long ws)
    {
        var list = new List<PickupInfo>();
        long folder = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.PooledDrops) : 0;
        if (folder == 0) return list;
        foreach (long d in reader.GetChildren(folder, 256))
        {
            if (!props.TryGetWorldPosition(d, out float x, out float y, out float z) || y < AnimeZeroPaths.PooledDropParkedY) continue;
            list.Add(new PickupInfo(reader.GetName(d), new(x, y, z)));
        }
        return list;
    }

    /// <summary>v0.2.67: Workspace.AriseShadows.&lt;n&gt;.Arise (ProximityPrompt Enabled) → parent Part position + HoldDuration.</summary>
    public static (int Count, int Max)? ParseShadows(string? t)
    {
        var m = t is null ? null : System.Text.RegularExpressions.Regex.Match(t, @"(\d+)\s*/\s*(\d+)");
        return m is { Success: true } && int.TryParse(m.Groups[1].Value, out int c) && int.TryParse(m.Groups[2].Value, out int x) && x > 0 ? (c, x) : null;
    }

    private List<AriseInfo> ReadArises(long ws)
    {
        var list = new List<AriseInfo>();
        long folder = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.AriseShadows) : 0;
        if (folder == 0) return list;
        foreach (long part in reader.GetChildren(folder, 64))
        {
            long pr = reader.FindChild(part, AnimeZeroPaths.ArisePrompt);
            if (pr == 0 || reader.GetClassName(pr) != "ProximityPrompt") continue;
            if (!props.TryGetWorldPosition(part, out float x, out float y, out float z)) continue;
            var p = props.Read(pr, "ProximityPrompt");
            if (p.TryGetValue("Enabled", out var en) && en.Equals("false", StringComparison.OrdinalIgnoreCase)) continue;
            double hold = p.TryGetValue("HoldDuration", out var h) ? Num(h, 0.9) : 0.9;
            double max = p.TryGetValue("MaxActivationDistance", out var md) ? Num(md, 10) : 10;
            string name = p.TryGetValue("ObjectText", out var ot) && !string.IsNullOrWhiteSpace(ot) ? ot : reader.GetName(part);
            list.Add(new AriseInfo(name, new(x, y, z), hold is > 0 and < 10 ? hold : 0.9, max is > 1 and < 100 ? max : 10));
        }
        return list;
    }

    /// <summary>v0.3.18: Trials Lift (World.Markers.Lift.ProximityPrompt). null = not in a Trials arena or unreadable.</summary>
    private LiftInfo? ReadLift(long ws, string? hudRaw)
    {
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        if (world == 0) return null;
        if (ReadPromptMarker(world, AnimeZeroPaths.LiftPath, AnimeZeroPaths.LiftPrompt, "Lift", false) is { } lift) return lift;
        // v0.3.42 Spirit's Bathtub: the elevator only counts while the HUD asks for it (proven text "Head to the elevator to continue!").
        return hudRaw?.Contains(AnimeZeroPaths.ElevatorHudWord, StringComparison.OrdinalIgnoreCase) == true
            ? ReadPromptMarker(world, AnimeZeroPaths.ElevatorPath, AnimeZeroPaths.ElevatorPrompt, "Elevator", true) : null;
    }

    private LiftInfo? ReadPromptMarker(long world, string path, string promptName, string fallbackAction, bool elevator)
    {
        long part = reader.ResolvePath(world, path);
        long pr = part != 0 ? reader.FindChild(part, promptName) : 0;
        if (pr == 0 || reader.GetClassName(pr) != "ProximityPrompt" || !props.TryGetWorldPosition(part, out float x, out float y, out float z)) return null;
        var p = props.Read(pr, "ProximityPrompt");
        if (!p.TryGetValue("Enabled", out var en)) return null; // unknown = do nothing
        double max = p.TryGetValue("MaxActivationDistance", out var md) ? Num(md, 10) : 10;
        double hold = p.TryGetValue("HoldDuration", out var h) ? Num(h, 0) : 0;
        string act = p.TryGetValue("ActionText", out var at) && !string.IsNullOrWhiteSpace(at) ? at : fallbackAction;
        return new LiftInfo(new(x, y, z), en.Equals("true", StringComparison.OrdinalIgnoreCase), max is > 1 and < 100 ? max : 10, act,
            hold is >= 0 and < 10 ? hold : 0, elevator);
    }

    /// <summary>Character HP (Character.Humanoid Health/MaxHealth) 0..1.</summary>
    private double? ReadPlayerHp(long localPlayer)
    {
        if (localPlayer == 0 || !reader.Offsets.TryResolve("Player.ModelInstance", out long mo)) return null;
        if (!M.TryReadPointer(localPlayer + mo, out long character) || character == 0) return null;
        long hum = reader.FindChild(character, "Humanoid");
        if (hum == 0) return null;
        var p = props.Read(hum, "Humanoid");
        if (!p.TryGetValue("Health", out var h) || !p.TryGetValue("MaxHealth", out var mh)) return null;
        double max = Num(mh, 0);
        return max > 0 ? Math.Clamp(Num(h, 0) / max, 0, 1) : null;
    }

    private CameraInfo? ReadCamera(long ws)
    {
        if (ws == 0 || !reader.Offsets.TryResolve("Workspace.CurrentCamera", out long co)) return null;
        if (!M.TryReadPointer(ws + co, out long cam) || cam == 0) return null;
        var p = props.Read(cam, "Camera");
        if (!p.TryGetValue("Position", out var pos) || !p.TryGetValue("LookVector", out var lv) || !p.TryGetValue("ViewportSize", out var vs)) return null;
        var a = Vec(pos); var l = Vec(lv); var v = Nums(vs);
        if (a is null || l is null || v.Length < 2) return null;
        double fov = p.TryGetValue("FieldOfView", out var f) ? Num(f, 70) : 70;
        string? subj = p.TryGetValue("CameraSubject", out var cs) && cs != "nil" && cs.Length > 0 ? cs : null;
        return new CameraInfo(a, l, fov, v[0], v[1]) { Subject = subj };
    }

    private static double[] Nums(string s) => s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(t => double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : double.NaN)
        .Where(d => !double.IsNaN(d)).ToArray();
    private static Settings.Vec3? Vec(string s) { var n = Nums(s); return n.Length == 3 ? new(n[0], n[1], n[2]) : null; }
    private static double Num(string s, double fallback) { var n = Nums(s); return n.Length > 0 ? n[0] : fallback; }

    /// <summary>Character HumanoidRootPart position: Player.ModelInstance → Character → HumanoidRootPart.</summary>
    private Settings.Vec3? ReadPlayerPos(long localPlayer)
    {
        if (localPlayer == 0 || !reader.Offsets.TryResolve("Player.ModelInstance", out long mo)) return null;
        if (!M.TryReadPointer(localPlayer + mo, out long character) || character == 0) return null;
        long hrp = reader.FindChild(character, "HumanoidRootPart");
        return hrp != 0 && props.TryGetWorldPosition(hrp, out float x, out float y, out float z) ? new(x, y, z) : null;
    }

    /// <summary>Camera LookVector (XZ) from Workspace.CurrentCamera.</summary>
    private (double X, double Z)? ReadCameraLook(long ws)
    {
        if (ws == 0 || !reader.Offsets.TryResolve("Workspace.CurrentCamera", out long co)) return null;
        if (!M.TryReadPointer(ws + co, out long cam) || cam == 0) return null;
        if (!props.Read(cam, "Camera").TryGetValue("LookVector", out var lv)) return null;
        var p = lv.Split(',', StringSplitOptions.TrimEntries);
        return p.Length == 3 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x)
               && double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double z)
            ? (x, z) : null;
    }

    /// <summary>Position of area n: Workspace.World.Spawns[n], then Areas[n] (Part, Model, or Folder containing Parts).</summary>
    private Settings.Vec3? WsPos(long ws, string path)
    {
        long part = ws != 0 ? reader.ResolvePath(ws, path) : 0;
        return part != 0 && props.TryGetWorldPosition(part, out float x, out float y, out float z) ? new(x, y, z) : null;
    }

    private Settings.Vec3? ReadAreaCenter(long ws, int n)
    {
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        long areas = world != 0 ? reader.FindChild(world, AnimeZeroPaths.WorldAreas) : 0;
        long part = areas != 0 ? reader.FindChild(areas, n.ToString()) : 0;
        return part != 0 && props.TryGetWorldPosition(part, out float x, out float y, out float z) ? new(x, y, z) : null;
    }

    private Settings.Vec3? ReadAreaPos(long ws, int n)
    {
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        if (world == 0) return null;
        Settings.Vec3? Pos(string folder)
        {
            long f = reader.FindChild(world, folder);
            long node = f != 0 ? reader.FindChild(f, n.ToString()) : 0;
            return node != 0 ? PositionOf(node) : null;
        }
        // v0.2.47: AreaEntries.<n> first (official entry point, same as the on-screen "AREA n" marker). Only exists for some areas.
        if (Pos(AnimeZeroPaths.WorldAreaEntries) is { } entry) return entry;
        // v0.2.50: Spawns.<n> is at the end of area n-1 (Hxh: Spawns.4 (158,-198) is still in the Area 3 box, AREA 4 marker ~150 studs further).
        // → point of the Areas.<n> box nearest Spawns.<n>, 8 studs inward = entry edge of area n.
        var spawn = Pos(AnimeZeroPaths.WorldSpawns);
        long areas = reader.FindChild(world, AnimeZeroPaths.WorldAreas);
        long part = areas != 0 ? reader.FindChild(areas, n.ToString()) : 0;
        if (part != 0 && props.TryGetWorldPosition(part, out float ax, out float ay, out float az))
        {
            var size = ParseVec(Prop(part, "Size"));
            var center = new Settings.Vec3(ax, ay, az);
            if (spawn is { } sp && size is { } bs && bs.X > 0) return AreaEntryPoint(center, bs, sp);
            return spawn ?? center;
        }
        return spawn;
    }

    /// <summary>Point of the area box (center/size) nearest <paramref name="from"/>, moved <paramref name="inset"/> studs inward. Y = Y of the origin point.</summary>
    public static Settings.Vec3 AreaEntryPoint(Settings.Vec3 center, Settings.Vec3 size, Settings.Vec3 from, double inset = 8)
    {
        double hx = Math.Max(0, size.X / 2 - inset), hz = Math.Max(0, size.Z / 2 - inset);
        double x = Math.Clamp(from.X, center.X - hx, center.X + hx), z = Math.Clamp(from.Z, center.Z - hz, center.Z + hz);
        return new(x, from.Y, z);
    }

    private Settings.Vec3? PositionOf(long node, int depth = 0)
    {
        if (props.TryGetWorldPosition(node, out float x, out float y, out float z)) return new(x, y, z);
        if (depth >= 2) return null;
        foreach (long c in reader.GetChildren(node, 64))
            if (PositionOf(c, depth + 1) is { } p) return p;
        return null;
    }
}

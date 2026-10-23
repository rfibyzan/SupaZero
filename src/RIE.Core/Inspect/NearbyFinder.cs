using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace RIE.Core.Inspect;

public sealed record NearbyHit(long Address, string Name, string ClassName, double Distance, string Path, string Info, float X, float Y, float Z)
{
    public string DistanceText => double.IsNaN(Distance) ? "-" : $"{Distance:0.0}";
}

/// <summary>Cari objek di Workspace (ProximityPrompt, ClickDetector, Part, ...) dan urutkan berdasarkan jarak dari karakter lokal.</summary>
public sealed class NearbyFinder(InstanceReader reader, PropertyReader props)
{
    public static readonly string[] Presets = { "ProximityPrompt", "ClickDetector", "BasePart (nama)", "Model (nama)", "Semua class (nama)" };

    public bool TryGetLocalPosition(DataModelRoots roots, out float x, out float y, out float z)
    {
        x = y = z = 0;
        if (roots.LocalPlayer == 0) return false;
        if (!reader.Offsets.TryResolve("Player.ModelInstance", out long mo) || !reader.Memory.TryReadPointer(roots.LocalPlayer + mo, out long character)) return false;
        long hrp = reader.FindChild(character, "HumanoidRootPart");
        return props.TryGetWorldPosition(hrp != 0 ? hrp : character, out x, out y, out z);
    }

    private static readonly HashSet<string> PartClasses = new(StringComparer.Ordinal)
    { "Part", "MeshPart", "UnionOperation", "WedgePart", "TrussPart", "Seat", "VehicleSeat", "SpawnLocation", "CornerWedgePart", "BasePart" };

    public List<NearbyHit> Find(DataModelRoots roots, int preset, string nameContains, int maxScan = 40000, int maxResults = 100, CancellationToken ct = default)
    {
        var hits = new List<NearbyHit>();
        long root = roots.Workspace != 0 ? roots.Workspace : roots.DataModel;
        if (root == 0) return hits;
        bool hasMe = TryGetLocalPosition(roots, out float mx, out float my, out float mz);
        string q = nameContains.Trim();
        var queue = new Queue<long>();
        var seen = new HashSet<long>();
        queue.Enqueue(root);
        int scanned = 0;
        while (queue.Count > 0 && scanned < maxScan && !ct.IsCancellationRequested)
        {
            long inst = queue.Dequeue();
            if (!seen.Add(inst)) continue;
            scanned++;
            foreach (var c in reader.GetChildren(inst)) queue.Enqueue(c);
            if (inst == root) continue;
            string cls = reader.GetClassName(inst);
            bool classOk = preset switch
            {
                0 => cls == "ProximityPrompt",
                1 => cls == "ClickDetector",
                2 => PartClasses.Contains(cls),
                3 => cls == "Model",
                _ => true,
            };
            if (!classOk) continue;
            string name = reader.GetName(inst);
            var info = string.Empty;
            if (cls == "ProximityPrompt")
            {
                var pr = props.Read(inst, cls);
                info = string.Join("  ", new[] { pr.GetValueOrDefault("ActionText"), pr.GetValueOrDefault("ObjectText"), pr.GetValueOrDefault("KeyCode") }.Where(s => !string.IsNullOrEmpty(s)));
            }
            string parentName = reader.GetName(reader.GetParent(inst));
            if (q.Length > 0 && !name.Contains(q, StringComparison.OrdinalIgnoreCase) && !parentName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !info.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            if (!props.TryGetWorldPosition(inst, out float x, out float y, out float z)) continue;
            double d = hasMe ? Math.Sqrt((x - mx) * (double)(x - mx) + (y - my) * (double)(y - my) + (z - mz) * (double)(z - mz)) : double.NaN;
            hits.Add(new NearbyHit(inst, name, cls, d, reader.GetFullPath(inst, roots.DataModel), info, x, y, z));
        }
        return hits.OrderBy(h => double.IsNaN(h.Distance) ? double.MaxValue : h.Distance).ThenBy(h => h.Name).Take(maxResults).ToList();
    }
}

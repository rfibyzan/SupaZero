using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace RIE.Core.Snapshot;

/// <summary>Walks a subtree (DFS) and records name, class, address, properties.</summary>
public sealed class SnapshotBuilder(InstanceReader reader, PropertyReader properties)
{
    public InstanceSnapshot Build(long root, string rootPath, SnapshotOptions? options = null, string label = "", CancellationToken ct = default)
    {
        options ??= new SnapshotOptions();
        var snap = new InstanceSnapshot
        {
            Label = label,
            TakenAt = DateTimeOffset.Now,
            RootPath = rootPath,
            RobloxVersion = reader.Offsets.RobloxVersion,
        };
        var visited = new HashSet<long>();
        int count = 0;
        bool truncated = false;
        snap.Root = Visit(root, rootPath, null, 0);
        snap.NodeCount = count;
        snap.Truncated = truncated;
        return snap;

        SnapshotNode Visit(long inst, string key, string? parentKey, int depth)
        {
            ct.ThrowIfCancellationRequested();
            count++;
            string cls = reader.GetClassName(inst);
            var node = new SnapshotNode
            {
                Key = key,
                ParentKey = parentKey,
                Name = reader.GetName(inst),
                ClassName = cls,
                Address = $"0x{inst:X}",
            };
            if (options.ReadProperties) node.Properties = properties.Read(inst, cls);
            if (!visited.Add(inst)) return node;
            if (depth >= options.MaxDepth) { truncated = true; return node; }
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (long child in reader.GetChildren(inst))
            {
                if (count >= options.MaxNodes) { truncated = true; break; }
                string name = reader.GetName(child);
                if (name.Length == 0) name = "<?>";
                int n = seen.TryGetValue(name, out int k) ? k + 1 : 1;
                seen[name] = n;
                string childKey = key + "." + name + (n > 1 ? $"[{n}]" : string.Empty);
                node.Children.Add(Visit(child, childKey, key, depth + 1));
            }
            return node;
        }
    }
}

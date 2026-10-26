namespace RIE.Core.Snapshot;

public enum DiffKind { Added, Removed, ClassChanged, PropertyChanged }

public sealed record DiffEntry(DiffKind Kind, string Key, string ClassName, string? Property = null, string? OldValue = null, string? NewValue = null);

public sealed class DiffResult
{
    public string BeforeLabel { get; init; } = string.Empty;
    public string AfterLabel { get; init; } = string.Empty;
    public DateTimeOffset BeforeAt { get; init; }
    public DateTimeOffset AfterAt { get; init; }
    public List<DiffEntry> Entries { get; init; } = new();
    public int Added => Entries.Count(e => e.Kind == DiffKind.Added);
    public int Removed => Entries.Count(e => e.Kind == DiffKind.Removed);
    public int Changed => Entries.Count(e => e.Kind is DiffKind.PropertyChanged or DiffKind.ClassChanged);
}

/// <summary>Compares two snapshots by Key. Removed instances are reported only at the root of the removed subtree.</summary>
public static class SnapshotDiff
{
    public static DiffResult Compare(InstanceSnapshot before, InstanceSnapshot after)
    {
        var a = before.Flatten().GroupBy(n => n.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var b = after.Flatten().GroupBy(n => n.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var entries = new List<DiffEntry>();

        foreach (var (key, nb) in b)
        {
            if (!a.TryGetValue(key, out var na))
            {
                if (!ParentMissing(nb, a)) entries.Add(new DiffEntry(DiffKind.Added, key, nb.ClassName));
                continue;
            }
            if (!string.Equals(na.ClassName, nb.ClassName, StringComparison.Ordinal))
                entries.Add(new DiffEntry(DiffKind.ClassChanged, key, nb.ClassName, "ClassName", na.ClassName, nb.ClassName));
            foreach (var prop in na.Properties.Keys.Union(nb.Properties.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                na.Properties.TryGetValue(prop, out var ov);
                nb.Properties.TryGetValue(prop, out var nv);
                if (!string.Equals(ov, nv, StringComparison.Ordinal))
                    entries.Add(new DiffEntry(DiffKind.PropertyChanged, key, nb.ClassName, prop, ov, nv));
            }
        }
        foreach (var (key, na) in a)
            if (!b.ContainsKey(key) && !ParentMissing(na, b))
                entries.Add(new DiffEntry(DiffKind.Removed, key, na.ClassName));

        entries.Sort((x, y) =>
        {
            int c = string.CompareOrdinal(x.Key, y.Key);
            return c != 0 ? c : x.Kind.CompareTo(y.Kind);
        });
        return new DiffResult
        {
            BeforeLabel = before.Label, AfterLabel = after.Label,
            BeforeAt = before.TakenAt, AfterAt = after.TakenAt, Entries = entries,
        };
    }

    /// <summary>True if the node's parent is missing from <paramref name="other"/> (reporting the parent is enough).</summary>
    private static bool ParentMissing(SnapshotNode node, Dictionary<string, SnapshotNode> other) =>
        node.ParentKey is not null && !other.ContainsKey(node.ParentKey);
}

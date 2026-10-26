namespace RIE.Core.Snapshot;

public sealed class SnapshotNode
{
    /// <summary>Stable key for diffing: path with duplicate-sibling indices, e.g. "PlayerGui.Main.Frame[2]".</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Parent key (null for the root).</summary>
    public string? ParentKey { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public SortedDictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);
    public List<SnapshotNode> Children { get; set; } = new();
}

public sealed class InstanceSnapshot
{
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset TakenAt { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public string RobloxVersion { get; set; } = string.Empty;
    public int NodeCount { get; set; }
    public bool Truncated { get; set; }
    public SnapshotNode Root { get; set; } = new();

    public IEnumerable<SnapshotNode> Flatten()
    {
        var stack = new Stack<SnapshotNode>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
    }
}

public sealed record SnapshotOptions(int MaxDepth = 64, int MaxNodes = 50_000, bool ReadProperties = true);

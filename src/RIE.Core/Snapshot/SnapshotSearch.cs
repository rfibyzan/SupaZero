namespace RIE.Core.Snapshot;

public enum SearchField { Any, Name, ClassName, Text }

/// <summary>Cari node di snapshot berdasarkan nama, class, atau nilai properti (contains, case-insensitive).</summary>
public static class SnapshotSearch
{
    public static IEnumerable<SnapshotNode> Find(InstanceSnapshot s, string query, SearchField field = SearchField.Any)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        foreach (var n in s.Flatten())
        {
            bool hit = field switch
            {
                SearchField.Name => Has(n.Name, query),
                SearchField.ClassName => Has(n.ClassName, query),
                SearchField.Text => n.Properties.Values.Any(v => Has(v, query)),
                _ => Has(n.Name, query) || Has(n.ClassName, query) || n.Properties.Values.Any(v => Has(v, query)),
            };
            if (hit) yield return n;
        }
    }

    private static bool Has(string s, string q) => s.Contains(q, StringComparison.OrdinalIgnoreCase);
}

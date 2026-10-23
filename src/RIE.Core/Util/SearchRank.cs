namespace RIE.Core.Util;

/// <summary>Peringkat dan pengelompokan hasil pencarian (murni, tanpa UI).</summary>
public static class SearchRank
{
    /// <summary>0 = nama sama persis, 1 = awalan, 2 = nama mengandung, 3 = cocok class/path, 4 = cocok teks/value.</summary>
    public static int Rank(string name, string className, string match, string query)
    {
        string q = query.Trim();
        if (q.Length == 0) return 3;
        int dot = q.LastIndexOf('.');
        string seg = dot >= 0 ? q[(dot + 1)..] : q;
        if (seg.Length > 0)
        {
            if (name.Equals(seg, StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.StartsWith(seg, StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.Contains(seg, StringComparison.OrdinalIgnoreCase)) return 2;
        }
        if (match.Length > 0) return 4;
        return 3;
    }

    /// <summary>Kedalaman path ("game.Workspace.A" = 2).</summary>
    public static int Depth(string path) => path.Count(c => c == '.');

    /// <summary>Path induk ("game.Players.A.B" → "game.Players.A"); kosong bila tidak ada.</summary>
    public static string ParentPath(string path) => path.LastIndexOf('.') is var i and > 0 ? path[..i] : string.Empty;

    /// <summary>Service teratas ("game.Players.A" → "Players").</summary>
    public static string Service(string path)
    {
        var parts = path.Split('.');
        if (parts.Length == 0) return string.Empty;
        int start = parts[0] == "game" ? 1 : 0;
        return start < parts.Length ? parts[start] : parts[0];
    }

    /// <summary>Path induk ringkas untuk header grup: "Players › TestUser › … › Slots" (tanpa "game").</summary>
    public static string GroupTitle(string parentPath, int maxSegments = 4)
    {
        var parts = parentPath.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && parts[0] == "game") parts.RemoveAt(0);
        if (parts.Count == 0) return "game";
        if (parts.Count > maxSegments) parts = new List<string> { parts[0], "…" }.Concat(parts.Skip(parts.Count - (maxSegments - 1))).ToList();
        return string.Join(" › ", parts);
    }

    /// <summary>Hitung kemunculan per kunci, urut terbanyak lalu nama.</summary>
    public static List<(string Key, int Count)> Facets(IEnumerable<string> keys) =>
        keys.Where(k => k.Length > 0).GroupBy(k => k, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(t => t.Item2).ThenBy(t => t.Key, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Urutan natural tanpa beda huruf besar/kecil: Slot2 sebelum Slot10.</summary>
    public static readonly IComparer<string> Natural = Comparer<string>.Create(CompareNatural);

    public static int CompareNatural(string? a, string? b)
    {
        a ??= string.Empty; b ??= string.Empty;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                string na = a[si..i].TrimStart('0'), nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                int c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}

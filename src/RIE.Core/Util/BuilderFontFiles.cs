namespace RIE.Core.Util;

/// <summary>
/// v3.7: locates Builder font files (the Roblox website/client font) on the user's PC.
/// Fonts are NOT included in the repo (the Builder licence is for Roblox UGC only); the app only loads
/// files already on the PC: the app's own fonts folder or a Roblox/Bloxstrap/Fishstrap install.
/// </summary>
public static class BuilderFontFiles
{
    public const string Sans = "Builder Sans";
    public const string Mono = "Builder Mono";

    /// <summary>Font family from the file name, or null if not Builder Sans/Mono (Extended is ignored).</summary>
    public static string? FamilyOf(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".otf" or ".ttf")) return null;
        string n = Path.GetFileNameWithoutExtension(fileName).Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        if (n.StartsWith("buildersans")) return Sans;
        if (n.StartsWith("buildermono")) return Mono;
        return null;
    }

    /// <summary>Candidate folders in order: the app fonts folder first, then the newest Roblox version per launcher.</summary>
    public static IReadOnlyList<string> CandidateDirs(string appFontsDir, string? localAppData, string? programFilesX86)
    {
        var dirs = new List<string> { appFontsDir };
        var roots = new List<string>();
        if (!string.IsNullOrEmpty(localAppData))
            foreach (var l in new[] { "Roblox", "Bloxstrap", "Fishstrap", "Voidstrap" })
                roots.Add(Path.Combine(localAppData, l, "Versions"));
        if (!string.IsNullOrEmpty(programFilesX86)) roots.Add(Path.Combine(programFilesX86, "Roblox", "Versions"));
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var versions = Directory.GetDirectories(root)
                    .Select(d => Path.Combine(d, "content", "fonts"))
                    .Where(Directory.Exists)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc);
                dirs.AddRange(versions);
            }
            catch { }
        }
        return dirs;
    }

    /// <summary>Builder files per family; for the same file name, the earlier folder wins.</summary>
    public static Dictionary<string, List<string>> Find(IEnumerable<string> dirs)
    {
        var result = new Dictionary<string, List<string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs)
        {
            string[] files;
            try { if (!Directory.Exists(dir)) continue; files = Directory.GetFiles(dir); }
            catch { continue; }
            foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (FamilyOf(f) is not { } fam || !seen.Add(Path.GetFileName(f))) continue;
                if (!result.TryGetValue(fam, out var list)) result[fam] = list = new List<string>();
                list.Add(f);
            }
        }
        return result;
    }

    /// <summary>Nearest weight, CSS-style: for &gt;= 500 look for heavier first, otherwise lighter first.</summary>
    public static int NearestWeight(IReadOnlyCollection<int> available, int wanted)
    {
        if (available.Count == 0) throw new ArgumentException("empty", nameof(available));
        if (available.Contains(wanted)) return wanted;
        bool heavierFirst = wanted >= 500;
        return available
            .OrderBy(w => Math.Abs(w - wanted))
            .ThenBy(w => heavierFirst ? (w > wanted ? 0 : 1) : (w < wanted ? 0 : 1))
            .First();
    }
}

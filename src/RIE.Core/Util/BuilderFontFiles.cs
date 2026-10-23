namespace RIE.Core.Util;

/// <summary>
/// v3.7: mencari file font Builder (font website/klien Roblox) di PC pengguna.
/// Font TIDAK disertakan di repo (lisensi Builder hanya untuk UGC Roblox); app hanya memuat
/// file yang sudah ada di PC: folder fonts milik app atau instalasi Roblox/Bloxstrap/Fishstrap.
/// </summary>
public static class BuilderFontFiles
{
    public const string Sans = "Builder Sans";
    public const string Mono = "Builder Mono";

    /// <summary>Keluarga font dari nama file, atau null bila bukan Builder Sans/Mono (Extended diabaikan).</summary>
    public static string? FamilyOf(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".otf" or ".ttf")) return null;
        string n = Path.GetFileNameWithoutExtension(fileName).Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        if (n.StartsWith("buildersans")) return Sans;
        if (n.StartsWith("buildermono")) return Mono;
        return null;
    }

    /// <summary>Folder kandidat berurutan: folder fonts app dulu, lalu versi Roblox terbaru per launcher.</summary>
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

    /// <summary>File Builder per keluarga; untuk nama file yang sama, folder yang lebih awal menang.</summary>
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

    /// <summary>Berat terdekat ala CSS: untuk &gt;= 500 cari yang lebih tebal dulu, selain itu yang lebih tipis dulu.</summary>
    public static int NearestWeight(IReadOnlyCollection<int> available, int wanted)
    {
        if (available.Count == 0) throw new ArgumentException("kosong", nameof(available));
        if (available.Contains(wanted)) return wanted;
        bool heavierFirst = wanted >= 500;
        return available
            .OrderBy(w => Math.Abs(w - wanted))
            .ThenBy(w => heavierFirst ? (w > wanted ? 0 : 1) : (w < wanted ? 0 : 1))
            .First();
    }
}

using System.Text.RegularExpressions;

namespace SupaZero.Core.Game;

/// <summary>Isi teks EnemyCount di RoundHUD, mis. "Easy · Area 3/5 · 8 left" atau "Story complete".</summary>
public sealed record HudInfo(string? Difficulty, int Area, int AreaTotal, int EnemiesLeft, bool StoryComplete, bool Enter = false)
{
    public bool IsFinalArea => AreaTotal > 0 && Area >= AreaTotal;
    /// <summary>
    /// Area yang harus dituju setelah bersih. "Enter area 4/5" (screenshot 24 Okt 2026, penanda "AREA 4 280 studs") = area 3 sudah
    /// bersih dan tujuan Area 4 → <see cref="Area"/> sendiri. "Area 3/5 · 0 left" = masih di area 3 → Area + 1. 0 = tidak ada.
    /// </summary>
    public int TargetArea => AreaTotal <= 0 ? 0 : Enter ? Area : Area < AreaTotal ? Area + 1 : 0;
}

public static partial class HudText
{
    [GeneratedRegex(@"Area\s*(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AreaRx();

    [GeneratedRegex(@"(\d+)\s*left", RegexOptions.IgnoreCase)]
    private static partial Regex LeftRx();

    public static HudInfo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim();
        if (t.Contains("complete", StringComparison.OrdinalIgnoreCase))
            return new HudInfo(null, 0, 0, 0, true);
        var parts = t.Split(['·', '|', '•'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? diff = parts.Length > 0 && !AreaRx().IsMatch(parts[0]) && !LeftRx().IsMatch(parts[0]) ? parts[0] : null;
        var a = AreaRx().Match(t);
        var l = LeftRx().Match(t);
        if (!a.Success && !l.Success) return null;
        return new HudInfo(diff,
            a.Success ? int.Parse(a.Groups[1].Value) : 0,
            a.Success ? int.Parse(a.Groups[2].Value) : 0,
            l.Success ? int.Parse(l.Groups[1].Value) : -1,
            false,
            Enter: Regex.IsMatch(t, @"\benter\s+area", RegexOptions.IgnoreCase));
    }
}

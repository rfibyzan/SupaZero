using System.Text.RegularExpressions;

namespace SupaZero.Core.Game;

/// <summary>Isi teks EnemyCount di RoundHUD, mis. "Easy · Area 3/5 · 8 left" atau "Story complete".</summary>
public sealed record HudInfo(string? Difficulty, int Area, int AreaTotal, int EnemiesLeft, bool StoryComplete)
{
    public bool IsFinalArea => AreaTotal > 0 && Area >= AreaTotal;
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
            false);
    }
}

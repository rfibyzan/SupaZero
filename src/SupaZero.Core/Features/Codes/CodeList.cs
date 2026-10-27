using System.Text.RegularExpressions;

namespace SupaZero.Core.Features.Codes;

public enum CodeOutcome { Redeemed, AlreadyUsed, Failed, NoResponse }

/// <summary>v0.3.26 (Byzan 27 Oct): Anime Zero codes from beebom.com/anime-zero-codes (active list only, above "Expired").</summary>
public static partial class CodeList
{
    public const string BeebomUrl = "https://beebom.com/anime-zero-codes/";

    [GeneratedRegex(@"<li>\s*<strong>\s*([A-Za-z0-9]{3,40})\s*</strong>\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ItemRx();

    public static List<string> ParseBeebom(string html)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(html)) return result;
        var first = ItemRx().Match(html);
        if (!first.Success) return result;
        int expired = html.IndexOf("Expired Anime Zero Codes", first.Index, StringComparison.OrdinalIgnoreCase);
        string active = expired > 0 ? html[..expired] : html;
        foreach (Match m in ItemRx().Matches(active))
        {
            string code = m.Groups[1].Value.Trim();
            if (!result.Contains(code, StringComparer.OrdinalIgnoreCase)) result.Add(code);
        }
        return result;
    }

    /// <summary>Extra codes typed by the user (comma / space / newline separated).</summary>
    public static List<string> ParseManual(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] :
        text.Split([',', ' ', '\n', '\r', ';', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim()).Where(c => c.Length >= 3 && c.All(char.IsAsciiLetterOrDigit)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static async Task<List<string>> FetchBeebomAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SupaZero");
        return ParseBeebom(await http.GetStringAsync(BeebomUrl, ct));
    }

    /// <summary>Toast text → outcome. 'Code "X" redeemed!' / 'You received …' = redeemed; 'already used' = used before; anything else = failed (kept for a later try).</summary>
    public static CodeOutcome Classify(string text)
    {
        if (text.Contains("already used", StringComparison.OrdinalIgnoreCase)) return CodeOutcome.AlreadyUsed;
        if (Regex.IsMatch(text, @"\bredeemed\b", RegexOptions.IgnoreCase) || text.StartsWith("You received", StringComparison.OrdinalIgnoreCase)) return CodeOutcome.Redeemed;
        return CodeOutcome.Failed;
    }
}

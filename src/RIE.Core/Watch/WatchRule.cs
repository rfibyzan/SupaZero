using System.Globalization;

namespace RIE.Core.Watch;

public enum RuleOp { Changed, Equals, NotEquals, Contains, NotContains, Greater, Less, GreaterOrEqual, LessOrEqual, Exists, Missing }

/// <summary>Condition on one property of a watched path. Example: Text Contains "Caught", Visible Equals true, Value Greater 50.</summary>
public sealed class WatchRule
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Property { get; set; } = "Text";
    public RuleOp Op { get; set; } = RuleOp.Changed;
    public string Value { get; set; } = string.Empty;

    public string Describe() => Op switch
    {
        RuleOp.Changed => $"{Path}.{Property} changed",
        RuleOp.Exists => $"{Path} exists",
        RuleOp.Missing => $"{Path} missing",
        _ => $"{Path}.{Property} {Op} \"{Value}\"",
    };

    /// <summary>Evaluates level conditions (not Changed). Changed is handled by WatchService.</summary>
    public bool Evaluate(IReadOnlyDictionary<string, string?> sample)
    {
        bool exists = sample.TryGetValue("Exists", out var ex) && ex == "true";
        if (Op == RuleOp.Exists) return exists;
        if (Op == RuleOp.Missing) return !exists;
        sample.TryGetValue(Property, out var cur);
        if (cur is null) return false;
        return Op switch
        {
            RuleOp.Equals => string.Equals(cur, Value, StringComparison.OrdinalIgnoreCase),
            RuleOp.NotEquals => !string.Equals(cur, Value, StringComparison.OrdinalIgnoreCase),
            RuleOp.Contains => cur.Contains(Value, StringComparison.OrdinalIgnoreCase),
            RuleOp.NotContains => !cur.Contains(Value, StringComparison.OrdinalIgnoreCase),
            RuleOp.Greater or RuleOp.Less or RuleOp.GreaterOrEqual or RuleOp.LessOrEqual => Compare(cur),
            _ => false,
        };
    }

    private bool Compare(string cur)
    {
        if (!TryNum(cur, out double a) || !TryNum(Value, out double b)) return false;
        return Op switch
        {
            RuleOp.Greater => a > b,
            RuleOp.Less => a < b,
            RuleOp.GreaterOrEqual => a >= b,
            _ => a <= b,
        };
    }

    /// <summary>Takes the first number from a string (e.g. "Coins: 120" → 120, "1, 2, 3" → 1).</summary>
    internal static bool TryNum(string s, out double v)
    {
        v = 0;
        int i = 0;
        while (i < s.Length && !(char.IsDigit(s[i]) || (s[i] is '-' or '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))) i++;
        int j = i;
        while (j < s.Length && (char.IsDigit(s[j]) || s[j] is '.' or '-' || (s[j] == ',' && j + 1 < s.Length && char.IsDigit(s[j + 1]) && s[j + 1] != ' '))) j++;
        return j > i && double.TryParse(s[i..j].Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}

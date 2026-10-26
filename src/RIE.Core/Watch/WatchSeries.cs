using System.Globalization;

namespace RIE.Core.Watch;

/// <summary>
/// Numeric history per "path.property" for sparklines. Only values readable as a single number
/// (Health, Value, Transparency, ChildCount, true/false → 1/0). Vector "x, y, z" is recorded as its length.
/// Ring buffer per series (default 120 points).
/// </summary>
public sealed class WatchSeries
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<double>> _series = new(StringComparer.Ordinal);

    public int Capacity { get; set; } = 120;

    public static bool TryParseNumber(string? value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string v = value.Trim();
        if (v == "true") { number = 1; return true; }
        if (v == "false") { number = 0; return true; }
        // "12 (Running)" → 12
        int sp = v.IndexOf(' ');
        if (sp > 0 && v.IndexOf(',') < 0) v = v[..sp];
        if (v.Contains(','))
        {
            // vector "x, y[, z]" (not UDim2, which uses braces)
            if (v.Contains('{')) return false;
            var parts = v.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length is < 2 or > 3) return false;
            double sum = 0;
            foreach (var p in parts)
            {
                if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double c)) return false;
                sum += c * c;
            }
            number = Math.Sqrt(sum);
            return true;
        }
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
    }

    public static string Key(string path, string property) => path + "." + property;

    public void Record(string path, IReadOnlyDictionary<string, string?> sample)
    {
        lock (_lock)
        {
            foreach (var (prop, val) in sample)
            {
                if (prop is "Exists" or "ClassName") continue;
                if (!TryParseNumber(val, out double n)) continue;
                string k = Key(path, prop);
                if (!_series.TryGetValue(k, out var q)) _series[k] = q = new Queue<double>();
                q.Enqueue(n);
                while (q.Count > Math.Max(2, Capacity)) q.Dequeue();
            }
        }
    }

    public void Remove(string path)
    {
        lock (_lock)
            foreach (var k in _series.Keys.Where(k => k.StartsWith(path + ".", StringComparison.Ordinal)).ToList())
                _series.Remove(k);
    }

    public void Clear() { lock (_lock) _series.Clear(); }

    /// <summary>Copy of all series. Only series whose value has ever changed if <paramref name="changingOnly"/>.</summary>
    public Dictionary<string, double[]> Snapshot(bool changingOnly = false)
    {
        lock (_lock)
        {
            var d = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var (k, q) in _series)
            {
                var arr = q.ToArray();
                if (changingOnly && (arr.Length < 2 || arr.All(x => x == arr[0]))) continue;
                d[k] = arr;
            }
            return d;
        }
    }
}

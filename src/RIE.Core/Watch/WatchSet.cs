using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RIE.Core.Watch;

/// <summary>Set Watch yang bisa disimpan/dimuat: path, rule, interval.</summary>
public sealed class WatchSet
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public List<string> Paths { get; set; } = new();
    public List<WatchRule> Rules { get; set; } = new();
    public int IntervalMs { get; set; } = 100;
    public bool RulesOnly { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static WatchSet FromJson(string json)
    {
        var s = JsonSerializer.Deserialize<WatchSet>(json, Json) ?? throw new InvalidDataException("Set Watch kosong.");
        s.Paths = s.Paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.StartsWith("game.", StringComparison.Ordinal) ? p[5..] : p).Distinct().ToList();
        s.IntervalMs = Math.Clamp(s.IntervalMs, 10, 60000);
        return s;
    }

    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var n = new string(name.Trim().Select(c => bad.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return n.Length == 0 ? "set" : n;
    }
}

/// <summary>Ekspor log Watch ke CSV (RFC 4180, koma, UTF-8).</summary>
public static class WatchCsv
{
    public const string Header = "waktu,jenis,path,properti,lama,baru,rule,durasi_ms";

    public static string ToCsv(IEnumerable<WatchEvent> events)
    {
        var sb = new StringBuilder().AppendLine(Header);
        foreach (var e in events)
            sb.Append(Esc(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))).Append(',')
              .Append(Esc(e.Kind.ToString())).Append(',').Append(Esc(e.Path)).Append(',').Append(Esc(e.Property)).Append(',')
              .Append(Esc(e.OldValue)).Append(',').Append(Esc(e.NewValue)).Append(',').Append(Esc(e.Rule)).Append(',')
              .Append(e.DurationMs?.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty).AppendLine();
        return sb.ToString();
    }

    public static string Esc(string? v)
    {
        if (string.IsNullOrEmpty(v)) return string.Empty;
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}

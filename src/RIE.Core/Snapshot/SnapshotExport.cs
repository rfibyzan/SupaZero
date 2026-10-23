using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RIE.Core.Snapshot;

public static class SnapshotExport
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(InstanceSnapshot s) => JsonSerializer.Serialize(s, Json);
    public static InstanceSnapshot FromJson(string json) => JsonSerializer.Deserialize<InstanceSnapshot>(json, Json)!;
    public static string ToJson(DiffResult d) => JsonSerializer.Serialize(new
    {
        d.BeforeLabel, d.AfterLabel, d.BeforeAt, d.AfterAt, d.Added, d.Removed, d.Changed, d.Entries,
    }, Json);

    /// <summary>Tree teks berindentasi: "Name [Class] {Prop=..., ...}".</summary>
    public static string ToText(InstanceSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Snapshot {s.Label} {s.RootPath} @ {s.TakenAt:yyyy-MM-dd HH:mm:ss zzz} ({s.NodeCount} node{(s.Truncated ? ", TERPOTONG" : "")}, {s.RobloxVersion})");
        Write(s.Root, 0);
        return sb.ToString();

        void Write(SnapshotNode n, int depth)
        {
            sb.Append(' ', depth * 2).Append(n.Name).Append(" [").Append(n.ClassName).Append(']');
            if (n.Properties.Count > 0)
                sb.Append(" {").Append(string.Join(", ", n.Properties.Select(p => $"{p.Key}={Quote(p.Value)}"))).Append('}');
            sb.AppendLine();
            foreach (var c in n.Children) Write(c, depth + 1);
        }
    }

    public static string ToText(DiffResult d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Diff {d.BeforeLabel} ({d.BeforeAt:HH:mm:ss}) -> {d.AfterLabel} ({d.AfterAt:HH:mm:ss}): +{d.Added} -{d.Removed} ~{d.Changed}");
        foreach (var e in d.Entries)
        {
            switch (e.Kind)
            {
                case DiffKind.Added: sb.AppendLine($"+ {e.Key} [{e.ClassName}]"); break;
                case DiffKind.Removed: sb.AppendLine($"- {e.Key} [{e.ClassName}]"); break;
                default: sb.AppendLine($"~ {e.Key} [{e.ClassName}] {e.Property}: {Quote(e.OldValue)} -> {Quote(e.NewValue)}"); break;
            }
        }
        return sb.ToString();
    }

    private static string Quote(string? v) => v is null ? "(none)" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
}

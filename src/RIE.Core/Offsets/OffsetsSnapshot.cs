using System.Text.Json;

namespace RIE.Core.Offsets;

/// <summary>
/// offsets.json (format dumper: {"Roblox Version":..., "Offsets":{"Class":{"Prop":123}}}).
/// Diturunkan dari OpenFisch.Core.Offsets.OffsetsSnapshot, tanpa shared-offsets/CatchRegistry.
/// </summary>
public sealed class OffsetsSnapshot
{
    public string Source { get; private init; } = string.Empty;
    public string RobloxVersion { get; private init; } = string.Empty;
    public string FilePath { get; private init; } = string.Empty;
    public int TotalOffsets { get; private init; }
    public IReadOnlyDictionary<string, Dictionary<string, long>> Groups { get; private init; } =
        new Dictionary<string, Dictionary<string, long>>();

    public static OffsetsSnapshot Load(string path)
    {
        var s = Parse(File.ReadAllText(path));
        return new OffsetsSnapshot
        {
            Source = s.Source, RobloxVersion = s.RobloxVersion, TotalOffsets = s.TotalOffsets,
            Groups = s.Groups, FilePath = Path.GetFullPath(path),
        };
    }

    public static OffsetsSnapshot Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        });
        var root = doc.RootElement;
        if (!root.TryGetProperty("Offsets", out var offsets) || offsets.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("offsets.json tidak memiliki objek \"Offsets\".");
        var groups = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        int total = 0;
        foreach (var group in offsets.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object) continue;
            var fields = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var field in group.Value.EnumerateObject())
            {
                if (TryNumber(field.Value, out long v)) { fields[field.Name] = v; total++; }
            }
            groups[group.Name] = fields;
        }
        return new OffsetsSnapshot
        {
            Source = Str(root, "Source"),
            RobloxVersion = Str(root, "Roblox Version"),
            TotalOffsets = total,
            Groups = groups,
        };
    }

    /// <summary>Terima angka JSON atau string hex ("0x1A8").</summary>
    private static bool TryNumber(JsonElement e, out long v)
    {
        v = 0;
        if (e.ValueKind == JsonValueKind.Number) return e.TryGetInt64(out v);
        if (e.ValueKind == JsonValueKind.String)
        {
            string s = e.GetString() ?? string.Empty;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(s.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out v);
            return long.TryParse(s, out v);
        }
        return false;
    }

    private static string Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : string.Empty;

    public bool Has(string group, string field) => Groups.TryGetValue(group, out var f) && f.ContainsKey(field);

    /// <summary>Resolve "Class.Prop" atau alias pendek (lihat <see cref="OffsetAliases"/>).</summary>
    public bool TryResolve(string name, out long offset)
    {
        if (OffsetAliases.Map.TryGetValue(name, out var target)) name = target;
        int dot = name.IndexOf('.');
        offset = 0;
        if (dot <= 0) return false;
        return Groups.TryGetValue(name[..dot], out var fields) && fields.TryGetValue(name[(dot + 1)..], out offset);
    }

    /// <summary>Coba beberapa kunci, kembalikan yang pertama ada.</summary>
    public bool TryResolveAny(out long offset, params string[] names)
    {
        foreach (var n in names) if (TryResolve(n, out offset)) return true;
        offset = 0;
        return false;
    }

    /// <summary>env RIE_OFFSETS_PATH → &lt;exe&gt;\offsets.json → %AppData%\RobloxInstanceExplorer\offsets.json.</summary>
    /// <summary>Lokasi offsets hasil "Unduh terbaru" (diprioritaskan di atas salinan bawaan di folder exe).</summary>
    public static string UserOffsetsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobloxInstanceExplorer", "offsets.json");

    public static string? FindDefaultPath()
    {
        string? env = Environment.GetEnvironmentVariable("RIE_OFFSETS_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        string[] candidates =
        [
            UserOffsetsPath,
            Path.Combine(AppContext.BaseDirectory, "offsets.json"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}

/// <summary>Alias pendek → kunci bertitik (subset generik dari tabel alias OpenFisch).</summary>
public static class OffsetAliases
{
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["FakeDataModelPointer"] = "FakeDataModel.Pointer",
        ["FakeDataModelToDataModel"] = "FakeDataModel.RealDataModel",
        ["LocalPlayer"] = "Player.LocalPlayer",
        ["Children"] = "Instance.ChildrenStart",
        ["ChildrenEnd"] = "Instance.ChildrenEnd",
        ["Name"] = "Instance.Name",
        ["Parent"] = "Instance.Parent",
        ["ClassDescriptor"] = "Instance.ClassDescriptor",
        ["ClassDescriptorToClassName"] = "Instance.ClassName",
        ["StringLength"] = "Misc.StringLength",
        ["Text"] = "GuiObject.Text",
        ["FrameVisible"] = "GuiObject.Visible",
        ["ScreenGuiEnabled"] = "GuiObject.ScreenGui_Enabled",
        ["AbsolutePosition"] = "GuiBase2D.AbsolutePosition",
        ["AbsoluteSize"] = "GuiBase2D.AbsoluteSize",
    };
}

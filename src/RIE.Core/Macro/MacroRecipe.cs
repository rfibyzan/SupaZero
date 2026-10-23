using System.Text.Json;
using RIE.Core.Snapshot;
using RIE.Core.Watch;

namespace RIE.Core.Macro;

/// <summary>Elemen yang dipakai macro: path stabil + posisi saat direkam.</summary>
public sealed class RecipeElement
{
    public string Alias { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public SortedDictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Rect relatif area klien Roblox (piksel). Null bila bukan GUI.</summary>
    public ScreenRect? ClientRect { get; set; }
    /// <summary>Pusat ternormalisasi 0..1 terhadap ukuran klien (tahan resize jendela untuk UI berbasis Scale).</summary>
    public double? NormCenterX { get; set; }
    public double? NormCenterY { get; set; }
    public string Note { get; set; } = string.Empty;
}

/// <summary>Resep macro: hasil riset yang dibaca proyek macro terpisah. App ini tidak mengeksekusinya.</summary>
public sealed class MacroRecipe
{
    public string Name { get; set; } = "resep";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string RobloxVersion { get; set; } = string.Empty;
    public string PlaceId { get; set; } = string.Empty;
    public WindowInfo Window { get; set; } = WindowInfo.None;
    public double GuiInsetY { get; set; } = ScreenMap.DefaultGuiInsetY;
    public double GuiScale { get; set; } = 1;
    public List<RecipeElement> Elements { get; set; } = new();
    public List<WatchRule> Rules { get; set; } = new();
    public string Notes { get; set; } = string.Empty;

    public RecipeElement AddElement(string path, string className, IReadOnlyDictionary<string, string> props, string? alias = null)
    {
        var e = new RecipeElement
        {
            Alias = alias ?? UniqueAlias(path),
            Path = path,
            ClassName = className,
            Properties = new SortedDictionary<string, string>(props.ToDictionary(k => k.Key, v => v.Value), StringComparer.Ordinal),
        };
        if (ScreenMap.TryFromProperties(props, GuiInsetY, GuiScale, out var rect))
        {
            e.ClientRect = rect;
            if (Window.IsValid)
            {
                e.NormCenterX = Math.Round(rect.CenterX / Window.ClientWidth, 4);
                e.NormCenterY = Math.Round(rect.CenterY / Window.ClientHeight, 4);
            }
        }
        Elements.RemoveAll(x => x.Path == path);
        Elements.Add(e);
        return e;
    }

    private string UniqueAlias(string path)
    {
        string baseName = path[(path.LastIndexOf('.') + 1)..];
        string alias = baseName;
        for (int i = 2; Elements.Any(e => e.Alias == alias && e.Path != path); i++) alias = baseName + i;
        return alias;
    }

    public string ToJson() => JsonSerializer.Serialize(this, SnapshotExport.Json);
    public static MacroRecipe FromJson(string json) => JsonSerializer.Deserialize<MacroRecipe>(json, SnapshotExport.Json)!;
}

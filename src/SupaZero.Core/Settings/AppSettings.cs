using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupaZero.Core.Settings;

/// <summary>All settings; saved to %AppData%\SupaZero\settings.json.</summary>
public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public CustomThemeSettings CustomTheme { get; set; } = new();
    public FarmSettings Farm { get; set; } = new();
    public CombatSettings Combat { get; set; } = new();
    public NavigationSettings Navigation { get; set; } = new();
    public UpgradeSettings Upgrade { get; set; } = new();
    public WebhookSettings Webhook { get; set; } = new();
    public string? OffsetsPath { get; set; }
    /// <summary>Power names (character id → display name) and per-power traits already read from the game.</summary>
    public Dictionary<string, string> PowerNames { get; set; } = new();
    public Dictionary<string, Game.PowerTrait> PowerTraits { get; set; } = new();
    public List<Game.EquippedPower> PowerLoadout { get; set; } = new();

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SupaZero");

    public static string DefaultPath => Path.Combine(DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) is { } loaded)
            {
                // Navigation toggles were removed from the UI → always on; arrive radius clamped to 2..20 studs.
                var n = loaded.Navigation;
                loaded.Navigation = n with
                {
                    AutoNavigate = true, AttackWhenStuck = true, ChaseEnemies = true, AimAtEnemy = true, RoamWhenSkillsCooling = true,
                    ArriveRadius = n.ArriveRadius is >= 2 and <= 20 ? n.ArriveRadius : 6,
                };
                // v0.3.16: English UI → legacy Indonesian skill names renamed (key, toggle and cooldown kept).
                if (loaded.Combat.Skills is { } sk)
                    loaded.Combat = loaded.Combat with { Skills = sk.Select(k => k.Name is "Ganti power" or "Ganti senjata" ? k with { Name = "Swap power" } : k).ToList() };
                return loaded;
            }
        }
        catch { /* corrupt file → defaults */ }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}

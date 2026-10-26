using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupaZero.Core.Settings;

/// <summary>Semua pengaturan; disimpan di %AppData%\SupaZero\settings.json.</summary>
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
    /// <summary>Nama power (id karakter → nama tampil) dan trait per power yang sudah terbaca dari game.</summary>
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
                // Toggle navigasi sudah dihapus dari UI → selalu aktif; radius sampai dibatasi 2..20 stud.
                var n = loaded.Navigation;
                loaded.Navigation = n with
                {
                    AutoNavigate = true, AttackWhenStuck = true, ChaseEnemies = true, AimAtEnemy = true, RoamWhenSkillsCooling = true,
                    ArriveRadius = n.ArriveRadius is >= 2 and <= 20 ? n.ArriveRadius : 6,
                };
                return loaded;
            }
        }
        catch { /* file rusak → default */ }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}

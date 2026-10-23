using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupaZero.Core.Settings;

/// <summary>Settings → Appearance → Custom colours (sama dengan SupaFisch).</summary>
public sealed record CustomThemeSettings(
    string Background = "#0A0A0A",
    string Surface = "#141414",
    string Border = "#262626",
    string Accent = "#3B82F6",
    string Text = "#EDEDED");

public sealed record GeneralSettings(
    string MacroHotkey = "F1",
    string Theme = "Black",
    bool TransparentUi = false,
    /// <summary>Jeda loop macro (ms). Tiap tick: baca snapshot memori → putuskan aksi → kirim input.</summary>
    int TickMs = 100,
    /// <summary>Tinggi topbar Roblox untuk konversi AbsolutePosition → klien (sama dengan kalibrasi RIE; 58 default).</summary>
    double GuiInsetY = 58,
    double GuiScale = 1);

public enum Difficulty { Easy, Medium, Hard, Nightmare }

/// <summary>Pilihan Story di CREATE LOBBY. Map kosong = pakai map yang sudah terpilih di lobby.</summary>
public sealed record StorySettings(
    string Map = "",
    int Act = 1,
    Difficulty Difficulty = Difficulty.Easy,
    bool AutoCreateLobby = true,
    bool AutoVoteStart = true,
    bool AutoNext = true,
    bool RetryOnDefeat = true,
    int MaxRetriesPerStage = 3);

public sealed record CombatSettings(
    int AttackIntervalMs = 150,
    bool UseSkills = true,
    string SkillKeys = "1,2,3",
    int SkillIntervalMs = 1200,
    bool UseDash = false,
    bool AutoSwitchPower = false);

public enum UpgradeMode
{
    /// <summary>Habiskan kolom prioritas pertama dulu, baru kolom berikutnya.</summary>
    Priority,
    /// <summary>Beli bergiliran satu slot per kolom mengikuti urutan prioritas.</summary>
    RoundRobin,
}

public sealed record UpgradeSettings(
    bool Enabled = false,
    UpgradeMode Mode = UpgradeMode.Priority,
    List<string>? Priority = null,
    int MaxBuysPerStage = 5)
{
    public static IReadOnlyList<string> Columns { get; } = ["ATK", "CRIT", "DEF", "SPD"];
    public IReadOnlyList<string> EffectivePriority =>
        Priority is { Count: > 0 } p ? p : ["ATK", "CRIT", "SPD", "DEF"];
}

/// <summary>Semua pengaturan; disimpan di %AppData%\SupaZero\settings.json.</summary>
public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public CustomThemeSettings CustomTheme { get; set; } = new();
    public StorySettings Story { get; set; } = new();
    public CombatSettings Combat { get; set; } = new();
    public UpgradeSettings Upgrade { get; set; } = new();
    public string? OffsetsPath { get; set; }

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
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
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

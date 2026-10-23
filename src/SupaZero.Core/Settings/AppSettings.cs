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
    /// <summary>Inset Y GUI hub lobby (tombol Areas, kartu teleport). Terverifikasi 0.</summary>
    double LobbyInsetY = 0,
    double GuiScale = 1);

public enum Difficulty { Easy, Medium, Hard, Nightmare }

/// <summary>Mode di menu Create Lobby (riset wiki/YouTube Okt 2026).</summary>
public enum FarmMode { Story, Infinite, Defend, Raid, Trials }

/// <summary>Aksi di EndScreen setelah menang.</summary>
public enum AfterWin
{
    /// <summary>Story → Next (lanjut stage berikutnya); mode lain → Retry.</summary>
    Auto,
    Next,
    Retry,
    Leave,
}

public sealed record Vec3(double X, double Y, double Z)
{
    public override string ToString() => $"{X:0.#}, {Y:0.#}, {Z:0.#}";
}

/// <summary>Satu map yang terbaca dari MapsList di Create Lobby (disimpan agar dropdown tetap terisi di luar lobby).</summary>
public sealed record KnownMap(string Name, bool Locked);

/// <summary>Pengaturan AutoFarm. Map kosong = pakai map yang sudah terpilih di lobby.</summary>
public sealed record FarmSettings(
    FarmMode Mode = FarmMode.Story,
    string Map = "",
    int Act = 1,
    Difficulty Difficulty = Difficulty.Easy,
    AfterWin AfterWin = AfterWin.Auto,
    bool AutoCreateLobby = true,
    /// <summary>Posisi lingkaran Create Room di lobby (direkam dari posisi karakter). null = belum direkam.</summary>
    Vec3? LobbyCircle = null,
    /// <summary>Lewati room yang sudah dibuat pemain lain ("Join Here" / "Players x/y").</summary>
    bool SkipOccupiedRooms = true,
    /// <summary>Path RIE tombol Areas / kartu Story (opsional, menimpa deteksi teks). Contoh: PlayerGui.HUD.Left.Areas</summary>
    string AreasButtonPath = "",
    string StoryCardPath = "",
    /// <summary>Sudah di lingkaran tapi menu Create Room tidak terbuka selama ini (detik) → coba lingkaran lain.</summary>
    int JoinTimeoutSec = 5,
    bool AutoVoteStart = true,
    bool RetryOnDefeat = true,
    int MaxRetriesPerStage = 3,
    List<KnownMap>? KnownMaps = null);

/// <summary>Satu tombol skill: tombol (nama HotkeyMap, mis. "1", "Q"), aktif, jeda antar-tekan.</summary>
public sealed record SkillBinding(string Name, string Key, bool Enabled, int CooldownMs);

public sealed record CombatSettings(
    int AttackIntervalMs = 150,
    List<SkillBinding>? Skills = null)
{
    public static List<SkillBinding> DefaultSkills() =>
    [
        new("Skill 1", "1", true, 4000),
        new("Skill 2", "2", true, 6000),
        new("Skill 3", "3", true, 8000),
        new("Dash", "Q", false, 3000),
        new("Ganti senjata", "T", false, 15000),
    ];

    public IReadOnlyList<SkillBinding> EffectiveSkills => Skills is { Count: > 0 } s ? s : DefaultSkills();
}

public sealed record NavigationSettings(
    bool AutoNavigate = true,
    double ArriveRadius = 6,
    /// <summary>Saat jalan tapi posisi tidak berubah (tersangkut rubble/boulder) → serang + lompat.</summary>
    bool AttackWhenStuck = true);

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
    public FarmSettings Farm { get; set; } = new();
    public CombatSettings Combat { get; set; } = new();
    public NavigationSettings Navigation { get; set; } = new();
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

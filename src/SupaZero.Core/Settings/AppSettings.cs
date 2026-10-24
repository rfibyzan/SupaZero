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
    double GuiScale = 1,
    /// <summary>Overlay status di atas jendela Roblox selama macro berjalan.</summary>
    bool ShowOverlay = true,
    /// <summary>Posisi overlay relatif (0..1) di jendela Roblox: 0 = menempel kiri/atas, 1 = kanan/bawah (lihat OverlayPlacement).</summary>
    double OverlayX = 1,
    double OverlayY = 0);

public enum Difficulty { Easy, Medium, Hard, Nightmare }

/// <summary>Mode bawaan sebelum label "Mode:" kartu map terbaca dari game. Mode sebenarnya diambil dari kartu (Story, Training, Raid, Tidal High Rise, Trials, Infinite, ...).</summary>
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
public sealed record KnownMap(string Name, bool Locked, string? Mode = null);

/// <summary>Pengaturan AutoFarm. Map kosong = pakai map yang sudah terpilih di lobby.</summary>
public sealed record FarmSettings(
    /// <summary>Nama mode persis seperti label "Mode:" di kartu map (string, bukan enum, karena game menambah mode baru).</summary>
    string Mode = "Story",
    string Map = "",
    /// <summary>1–5 = act tetap; 0 = semua act berurutan (lanjut dari act terakhir yang tuntas per map).</summary>
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
    /// <summary>Mode Infinite: Retry tanpa batas (Maks retry per stage diabaikan).</summary>
    bool InfiniteUnlimitedRetry = true,
    List<KnownMap>? KnownMaps = null,
    /// <summary>Act terakhir yang tuntas per map (mode "Semua act").</summary>
    Dictionary<string, int>? ActProgress = null,
    /// <summary>Act yang tersedia per map (terbaca dari ActsList saat map itu terpilih).</summary>
    Dictionary<string, List<int>>? KnownActs = null,
    /// <summary>Drop Target: nama material di panel Rewards yang ditarget (Hard x2 / Nightmare x3). Kosong = mati.</summary>
    string DropTarget = "",
    /// <summary>Urutan pilihan kartu Mirror Blessing (nama dipisah koma). Tidak cocok → rarity tertinggi.</summary>
    string BlessingPriority = "Sharper Edge, Shard Snack, Big Heart, Chain Shatter, Swift Steps",
    /// <summary>Floor Trials yang dipilih di panel (0 = biarkan pilihan game / floor berikutnya).</summary>
    int TrialsFloor = 0,
    /// <summary>Sekali tiap Start, di lobby sebelum Create Room: buka menu Trait lalu Spin (tombol LobbyHUD) untuk membaca power + trait terpasang.</summary>
    bool CheckPowersInLobby = true);

/// <summary>Satu tombol skill: tombol (nama HotkeyMap, mis. "1", "Q"), aktif, jeda antar-tekan.</summary>
/// <summary>Discord webhook: notifikasi menang/kalah (dengan drop) dan macro berhenti.</summary>
public sealed record WebhookSettings(string Url = "", bool OnVictory = true, bool OnDefeat = true, bool OnStop = true, string Mention = "", bool OnSpin = true);

public sealed record SkillBinding(string Name, string Key, bool Enabled, int CooldownMs);

public sealed record CombatSettings(
    int AttackIntervalMs = 150,
    List<SkillBinding>? Skills = null,
    /// <summary>v0.2.67: Shadow Lord: dekati mayat berprompt ARISE lalu tahan E sampai bangkit jadi sekutu.</summary>
    bool AutoArise = true)
{
    public static List<SkillBinding> DefaultSkills() =>
    [
        new("Skill 1", "1", true, 4000),
        new("Skill 2", "2", true, 6000),
        new("Skill 3", "3", true, 8000),
        new("Dash", "Q", true, 3000),
        new("Ganti power", "T", true, 15000),
        new("Ultimate", "G", true, 1500),
    ];

    /// <summary>v0.2.65: pengaturan lama tanpa baris Ultimate (G) otomatis ditambah.</summary>
    public IReadOnlyList<SkillBinding> EffectiveSkills => Skills is { Count: > 0 } s
        ? (s.Any(k => k.Name == "Ultimate") ? s : [.. s, DefaultSkills().First(k => k.Name == "Ultimate")])
        : DefaultSkills();
}

public sealed record NavigationSettings(
    bool AutoNavigate = true,
    double ArriveRadius = 6,
    /// <summary>Saat jalan tapi posisi tidak berubah (tersangkut rubble/boulder) → serang + lompat.</summary>
    bool AttackWhenStuck = true,
    /// <summary>Saat bertarung: jalan mendekati musuh terdekat sampai jarak serang.</summary>
    bool ChaseEnemies = true,
    /// <summary>Jarak serang ke musuh (stud).</summary>
    double AttackRange = 6,
    /// <summary>Kursor diarahkan ke musuh target (proyeksi kamera).</summary>
    bool AimAtEnemy = true,
    /// <summary>Piksel gerak mouse per derajat putaran kamera (shift lock), dikoreksi tiap tick.</summary>
    double TurnPixelsPerDegree = 4,
    /// <summary>Saat sudah dekat musuh: jalan melingkari musuh sambil lompat + basic attack.</summary>
    bool RoamWhenSkillsCooling = true);

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

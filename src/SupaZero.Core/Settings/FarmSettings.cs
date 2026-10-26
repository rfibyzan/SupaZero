namespace SupaZero.Core.Settings;

/// <summary>Pengaturan farm: Lobby (room, vote, retry) + Modes (Drop Target, Mirror Blessing, Trials) + data kalibrasi. Nama properti = kunci JSON settings.json, jangan diganti.</summary>
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
    bool CheckPowersInLobby = true,
    /// <summary>v0.3.4: material yang bisa ditarget per "Map|Act N" (dari Kalibrasi aksi / menu Create Room di Hard/Nightmare).</summary>
    Dictionary<string, List<string>>? KnownDrops = null,
    /// <summary>v0.3.5: nama material yang dipelajari ("icon:…"/"key:materialNNN" → nama), disimpan antar-sesi.</summary>
    Dictionary<string, string>? KnownMaterialNames = null,
    /// <summary>v0.3.11: urutan buff Mirror Blessing (aktif = diprioritaskan). null = semua mati.</summary>
    List<SupaZero.Core.Game.BuffPref>? BlessingBuffs = null,
    /// <summary>v0.3.11: urutan kartu Mirror Blessing + boleh dipilih. null = dari BlessingPriority lama.</summary>
    List<SupaZero.Core.Game.BlessingPref>? BlessingCards = null,
    /// <summary>v0.3.11: kartu yang terbaca di game (nama, rarity, deskripsi, value) di luar daftar bawaan.</summary>
    List<SupaZero.Core.Game.BlessingInfo>? KnownBlessings = null);

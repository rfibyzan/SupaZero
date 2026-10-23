namespace SupaZero.Core.Game;

/// <summary>Kotak GUI dalam koordinat klien jendela Roblox (piksel).</summary>
public readonly record struct GuiRect(double X, double Y, double W, double H)
{
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
    public bool IsEmpty => W <= 0 || H <= 0;
    public double Bottom => Y + H;
    public double Right => X + W;
    public bool Contains(double x, double y, double margin = 0) =>
        x >= X + margin && x <= X + W - margin && y >= Y + margin && y <= Y + H - margin;
}

public enum GameScreen { Unknown, Lobby, LobbyMenu, StartVote, InRound, Victory, Defeat }

public sealed record SkillSlot(string Column, int Index, bool CanBuy, GuiRect? Rect);

/// <summary>Satu entri daftar (map/act/mode) di Create Lobby.</summary>
public sealed record LobbyEntry(string Name, bool Locked, bool Selected, GuiRect? Rect, string? Mode = null);

/// <summary>Satu bacaan keadaan game (read-only) per tick macro.</summary>
/// <summary>Satu room antrean (Workspace.Systems.QuqueRooms.&lt;n&gt;). Occupied = RoomGui menampilkan "Join Here"/"Players x/y" (sudah ada pemain lain).</summary>
public sealed record StartRoom(string Id, Settings.Vec3 Pos, bool Occupied, int? Players = null, int? MaxPlayers = null);

public sealed record GameSnapshot
{
    public bool Attached { get; init; }
    public long PlaceId { get; init; }
    public string LocalPlayer { get; init; } = string.Empty;

    // Karakter + kamera (untuk Navigator)
    public Settings.Vec3? PlayerPos { get; init; }
    public (double X, double Z)? CameraLook { get; init; }
    /// <summary>Posisi area berikutnya (Workspace.World.Spawns/Areas[Area+1]).</summary>
    public Settings.Vec3? NextAreaPos { get; init; }

    // Lobby hub: tombol Areas (HUD kiri) → panel MATCHES "Click to Teleport" → dekat lingkaran "Start Here".
    public GuiRect? AreasButton { get; init; }
    /// <summary>Kartu teleport di panel Areas (Name = judul kartu, mis. "Story").</summary>
    public IReadOnlyList<LobbyEntry> TeleportCards { get; init; } = [];
    /// <summary>Lingkaran Create Room terdeteksi: part yang memuat teks "Start Here" di Workspace.</summary>
    public IReadOnlyList<Settings.Vec3> StartCircles { get; init; } = [];
    /// <summary>Room dari QuqueRooms.*.JoinHitbox + status terisi (dibaca ulang tiap tick).</summary>
    public IReadOnlyList<StartRoom> StartRooms { get; init; } = [];

    // Lobby
    public bool LobbyMenuOpen { get; init; }
    public IReadOnlyList<LobbyEntry> LobbyModes { get; init; } = [];
    public IReadOnlyList<LobbyEntry> LobbyMaps { get; init; } = [];
    public IReadOnlyList<LobbyEntry> LobbyActs { get; init; } = [];
    public string? LobbyMapTag { get; init; }
    public string? LobbyActTag { get; init; }
    /// <summary>Difficulty terpilih dari MapLine.Tag ("[HARD]" → "Hard"), null bila tidak terbaca.</summary>
    public string? LobbyDifficultyTag { get; init; }
    public GuiRect? LobbyStart { get; init; }
    /// <summary>Area tampak ScrollingFrame daftar map/act/mode (untuk cek apakah entri perlu di-scroll dulu).</summary>
    public GuiRect? LobbyMapsView { get; init; }
    public GuiRect? LobbyActsView { get; init; }
    public GuiRect? LobbyModesView { get; init; }
    /// <summary>Tombol difficulty yang terbaca dari teks Easy/Medium/Hard/Nightmare di menu Create Room.</summary>
    public IReadOnlyList<LobbyEntry> LobbyDifficulties { get; init; } = [];

    /// <summary>Ukuran area klien jendela Roblox (diisi MacroRunner). 0 = tidak diketahui.</summary>
    public int ClientW { get; init; }
    public int ClientH { get; init; }

    // Ronde
    public bool StartVoteVisible { get; init; }
    public GuiRect? StartVoteYes { get; init; }
    public string? HudRaw { get; init; }
    public HudInfo? Hud { get; init; }
    /// <summary>Jumlah anak Workspace.enemies; -1 = folder tidak ada.</summary>
    public int EnemyCount { get; init; } = -1;
    public bool BossBarVisible { get; init; }

    // Layar akhir
    public bool EndScreenVisible { get; init; }
    public string? ResultText { get; init; }
    public GuiRect? NextButton { get; init; }
    public GuiRect? RetryButton { get; init; }
    public GuiRect? LeaveButton { get; init; }

    // Upgrade
    public bool SkillTreeOpen { get; init; }
    public int? Tokens { get; init; }
    public IReadOnlyList<SkillSlot> Slots { get; init; } = [];
    public GuiRect? BuyButton { get; init; }

    // Power di loadout (lobby; di stage memakai hasil lobby terakhir)
    public IReadOnlyList<EquippedPower> Equipped { get; init; } = [];

    public bool InRound => Hud is not null || EnemyCount >= 0;

    public bool IsDefeat => ResultText?.Contains("DEFEAT", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Klasifikasi layar. Catatan riset: saat kalah EnemyCount tetap "Story complete", jadi menang hanya dari EndScreen
    /// tanpa ResultText DEFEAT.
    /// </summary>
    public GameScreen Screen
    {
        get
        {
            if (!Attached) return GameScreen.Unknown;
            if (EndScreenVisible || IsDefeat) return IsDefeat ? GameScreen.Defeat : GameScreen.Victory;
            if (StartVoteVisible) return GameScreen.StartVote;
            if (LobbyMenuOpen) return GameScreen.LobbyMenu;
            if (InRound) return GameScreen.InRound;
            return GameScreen.Lobby;
        }
    }
}

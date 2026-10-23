namespace SupaZero.Core.Game;

/// <summary>Kotak GUI dalam koordinat klien jendela Roblox (piksel).</summary>
public readonly record struct GuiRect(double X, double Y, double W, double H)
{
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
    public bool IsEmpty => W <= 0 || H <= 0;
}

public enum GameScreen { Unknown, Lobby, LobbyMenu, StartVote, InRound, Victory, Defeat }

public sealed record SkillSlot(string Column, int Index, bool CanBuy, GuiRect? Rect);

/// <summary>Satu entri daftar (map/act/mode) di Create Lobby.</summary>
public sealed record LobbyEntry(string Name, bool Locked, bool Selected, GuiRect? Rect);

/// <summary>Satu bacaan keadaan game (read-only) per tick macro.</summary>
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

    // Lobby
    public bool LobbyMenuOpen { get; init; }
    public IReadOnlyList<LobbyEntry> LobbyModes { get; init; } = [];
    public IReadOnlyList<LobbyEntry> LobbyMaps { get; init; } = [];
    public IReadOnlyList<LobbyEntry> LobbyActs { get; init; } = [];
    public string? LobbyMapTag { get; init; }
    public string? LobbyActTag { get; init; }
    public GuiRect? LobbyStart { get; init; }

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

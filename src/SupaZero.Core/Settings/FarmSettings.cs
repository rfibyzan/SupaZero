namespace SupaZero.Core.Settings;

/// <summary>Farm settings: Lobby (room, vote, retry) + Modes (Drop Target, Mirror Blessing, Trials) + calibration data. Property names = settings.json JSON keys, do not rename.</summary>
public enum Difficulty { Easy, Medium, Hard, Nightmare }

/// <summary>Default mode before the map card's "Mode:" label is read from the game. The actual mode is taken from the card (Story, Training, Raid, Tidal High Rise, Trials, Infinite, ...).</summary>
public enum FarmMode { Story, Infinite, Defend, Raid, Trials }

/// <summary>Action on the EndScreen after a win.</summary>
public enum AfterWin
{
    /// <summary>Story → Next (continue to the next stage); other modes → Retry.</summary>
    Auto,
    Next,
    Retry,
    Leave,
}

public sealed record Vec3(double X, double Y, double Z)
{
    public override string ToString() => $"{X:0.#}, {Y:0.#}, {Z:0.#}";
}

/// <summary>One map read from MapsList in Create Lobby (saved so the dropdown stays filled outside the lobby).</summary>
public sealed record KnownMap(string Name, bool Locked, string? Mode = null);

/// <summary>AutoFarm settings. Empty map = use the map already selected in the lobby.</summary>
public sealed record FarmSettings(
    /// <summary>Mode name exactly as the "Mode:" label on the map card (string, not enum, because the game adds new modes).</summary>
    string Mode = "Story",
    string Map = "",
    /// <summary>1–5 = fixed act; 0 = all acts in order (continue from the last cleared act per map).</summary>
    int Act = 1,
    Difficulty Difficulty = Difficulty.Easy,
    AfterWin AfterWin = AfterWin.Auto,
    bool AutoCreateLobby = true,
    /// <summary>Position of the Create Room circle in the lobby (recorded from the character position). null = not recorded yet.</summary>
    Vec3? LobbyCircle = null,
    /// <summary>Skip rooms already created by other players ("Join Here" / "Players x/y").</summary>
    bool SkipOccupiedRooms = true,
    /// <summary>RIE path of the Areas button / Story card (optional, overrides text detection). Example: PlayerGui.HUD.Left.Areas</summary>
    string AreasButtonPath = "",
    string StoryCardPath = "",
    /// <summary>Standing in the circle but the Create Room menu has not opened for this long (seconds) → try another circle.</summary>
    int JoinTimeoutSec = 5,
    bool AutoVoteStart = true,
    bool RetryOnDefeat = true,
    int MaxRetriesPerStage = 3,
    /// <summary>Infinite mode: unlimited Retry (max retries per stage is ignored).</summary>
    bool InfiniteUnlimitedRetry = true,
    List<KnownMap>? KnownMaps = null,
    /// <summary>Last cleared act per map ("All acts" mode).</summary>
    Dictionary<string, int>? ActProgress = null,
    /// <summary>Available acts per map (read from ActsList while that map is selected).</summary>
    Dictionary<string, List<int>>? KnownActs = null,
    /// <summary>Drop Target: name of the targeted material in the Rewards panel (Hard x2 / Nightmare x3). Empty = off.</summary>
    string DropTarget = "",
    /// <summary>Mirror Blessing card pick order (comma-separated names). No match → highest rarity.</summary>
    string BlessingPriority = "Sharper Edge, Shard Snack, Big Heart, Chain Shatter, Swift Steps",
    /// <summary>Trials floor to select in the panel (0 = keep the game's choice / next floor).</summary>
    int TrialsFloor = 0,
    /// <summary>Once per Start, in the lobby before Create Room: open the Trait menu then Spin (LobbyHUD buttons) to read the equipped powers + traits.</summary>
    bool CheckPowersInLobby = true,
    /// <summary>v0.3.4: targetable materials per "Map|Act N" (from action Calibration / the Create Room menu on Hard/Nightmare).</summary>
    Dictionary<string, List<string>>? KnownDrops = null,
    /// <summary>v0.3.5: learned material names ("icon:…"/"key:materialNNN" → name), persisted across sessions.</summary>
    Dictionary<string, string>? KnownMaterialNames = null,
    /// <summary>v0.3.11: Mirror Blessing buff order (enabled = prioritized). null = all off.</summary>
    List<SupaZero.Core.Game.BuffPref>? BlessingBuffs = null,
    /// <summary>v0.3.11: Mirror Blessing card order + whether pickable. null = from the legacy BlessingPriority.</summary>
    List<SupaZero.Core.Game.BlessingPref>? BlessingCards = null,
    /// <summary>v0.3.11: cards read in game (name, rarity, description, value) outside the built-in list.</summary>
    List<SupaZero.Core.Game.BlessingInfo>? KnownBlessings = null,
    /// <summary>v0.3.26: in the lobby, redeem codes (beebom list + ExtraCodes) not yet redeemed/used by this account, then continue.</summary>
    bool AutoRedeemCodes = true,
    /// <summary>v0.3.26: extra codes (comma separated) tried besides the beebom list.</summary>
    string ExtraCodes = "",
    /// <summary>v0.3.26: per player name, codes the game answered "redeemed" or "already used" (never typed again).</summary>
    Dictionary<string, List<string>>? DoneCodes = null);

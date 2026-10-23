namespace SupaZero.Core.Game;

/// <summary>
/// Path instance Anime Zero hasil riset RIE fase 1 (place 114574503491412). Yang ditandai [V] perlu diverifikasi ulang di
/// RIE: path persisnya belum tercatat, jadi <see cref="GameReader"/> mencarinya per nama di bawah PlayerGui (dibatasi
/// kedalaman) lalu menyimpan alamatnya di cache.
/// </summary>
public static class AnimeZeroPaths
{
    public const long PlaceId = 114574503491412;

    // Workspace
    public const string Enemies = "enemies";            // Workspace.enemies: Model bernama angka (HRP + Humanoid); mati = Model dihapus
    public const string World = "World";                // Workspace.World: Areas 1-5, Spawns 1-5, Navigation, TpBack
    public const string WorldAreas = "Areas";
    public const string WorldSpawns = "Spawns";
    public const string WorldNavigation = "Navigation"; // part rintangan (boulder dll.) [V]

    // PlayerGui: HUD ronde
    public const string RoundHud = "RoundHUD";
    public const string EnemyCountPath = "RoundHUD.Container.TopPart.RemainingFrame.EnemyCount"; // "Easy · Area 3/5 · 8 left"
    public const string TimerName = "Timer";
    public const string RespawnText = "RespawnText";

    // Vote mulai (tiap awal stage, timer 48 dtk). Tombol Yes ~(960,232) di 1920x1080.
    public const string StartVote = "StartVote";        // [V] nama frame
    public const string StartVoteYes = "Yes";           // [V]
    public const double StartVoteYesX = 960, StartVoteYesY = 232;

    // Layar akhir
    public const string EndScreen = "EndScreen";        // [V]
    public const string ResultText = "ResultText";      // "DEFEAT" saat kalah
    public const string NextButton = "Next";            // [V]
    public const string RetryButton = "Retry";          // [V]
    public const string LeaveButton = "Leave";          // [V]
    public const string BossBar = "BossBar";

    // Lobby CREATE LOBBY
    public const string LobbyRoot = "UI Animation (Finished).Quque.MainFrame";
    // Hub lobby (screenshot Byzan 23 Okt 2026): tombol HUD "Areas" → kartu "Click to Teleport" → lingkaran "Start Here".
    public const string AreasButtonText = "Areas";
    public const string TeleportCardHint = "Click to Teleport";
    public const string StartCircleText = "Start Here";
    /// <summary>Terverifikasi RIE (Byzan, 23 Okt 2026): Workspace.Systems.QuqueRooms.&lt;n&gt;.JoinHitbox (Part 18×25×25, CanCollide false).</summary>
    public const string Systems = "Systems";
    public const string QueueRooms = "QuqueRooms"; // typo asli game
    public const string JoinHitbox = "JoinHitbox";

    public const string LobbyModesList = "ModesList";   // [V]
    public const string LobbyMapsList = "MapsList";
    public const string LobbyActsList = "ActsList";
    public const string LobbyMapLine = "MapLine";       // .Tag = map terpilih
    public const string LobbyActLine = "ActLine";       // .Tag = act terpilih
    public const string LobbyStartButton = "StaerButton"; // (sic) ~(1364,934)
    public const string LobbyLeaveButton = "Leave";       // ~(1639,934)
    public static readonly (double X, double Y)[] DifficultyCenters = [(1033, 589), (1147, 589), (1261, 589), (1374, 589)];

    // SkillTree per unit
    public const string SkillTreeRoot = "UI Animation (Working).SkillTree";
    public const string SkillTreeTokens = "Header.Holder.Txt";
    public const string SlotCanBuy = "CanBuy";
    public const string BuyFrame = "BuyFrame";
    public const string BuyButton = "BuyButton";

    /// <summary>Resolusi referensi koordinat fallback di atas (layar 1920x1080).</summary>
    public const double RefWidth = 1920, RefHeight = 1080;
}

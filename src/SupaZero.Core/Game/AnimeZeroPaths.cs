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
    /// <summary>v0.2.67 (Shadow Lord): Workspace.AriseShadows.&lt;n&gt; (Part di lantai mayat) .Arise ProximityPrompt
    /// (ActionText "ARISE", ObjectText = nama musuh, HoldDuration 0.9, KeyCode E, MaxActivationDistance 10, tanpa line of sight).</summary>
    public const string AriseShadows = "AriseShadows";
    public const string ArisePrompt = "Arise";
    public const string PooledDrops = "PooledDrops";    // [V] S15: Workspace.PooledDrops.{cash,health} Part; terparkir di Y=-5000, aktif = di lantai. Dipungut cukup dilewati.
    public const double PooledDropParkedY = -1000;
    public const string Enemies = "enemies";            // Workspace.enemies: Model bernama angka (HRP + Humanoid); mati = Model dihapus
    public const string WorldMap = "Map";               // [V] S16: Part CanCollide (AlleyBarrier1-4 tembus pandang 54×60×5) = dinding
    public const string World = "World";                // Workspace.World: Areas 1-5, Spawns 1-5, Navigation, TpBack
    public const string WorldAreas = "Areas";
    public const string WorldNavigation = "Navigation"; // [V] S15 Hxh: jejak rintangan (Part datar Y=3); ER2: node "navN" 4×4×4 (tangga)
    public static readonly string[] WorldBarrierFolders = ["Barriers", "NpcBarriers"]; // [V] ada (kosong di S15)
    public const string WorldSpawns = "Spawns";
    public const string WorldAreaEntries = "AreaEntries"; // [V] S1 24 Okt 2026 Hxh: AreaEntries.5 (295,2,-53) = titik penanda game "AREA 5 n studs"

    // PlayerGui: HUD ronde
    public const string RoundHud = "RoundHUD";
    public const string EnemyCountPath = "RoundHUD.Container.TopPart.RemainingFrame.EnemyCount"; // "Easy · Area 3/5 · 8 left"
    public const string TimerName = "Timer";
    public const string RespawnText = "RespawnText";

    // Vote mulai (tiap awal stage, timer 48 dtk). Tombol Yes ~(960,232) di 1920x1080.
    public const string StartVote = "StartVote";        // [V] nama frame
    public const string StartVoteYes = "Yes";           // [V]
    public const string StartVoteButtonPath = "Holder.Main.StartButton"; // [V] snapshot 24 Okt 2026, ImageButton ~(960,232)
    public const string StartVoteButton = "StartButton";
    // Rubble (screenshot + snapshot PlayerGui 24 Okt 2026): HUD "Break the rubble to continue!", BillboardGui PlayerGui.RubbleBar
    // "Rubble [BREAK IT]" (Adornee = bagian rubble di world), ScreenGui PlayerGui.RubbleBossBar (Enabled saat rubble besar).
    public const string RubbleBar = "RubbleBar";
    public const string RubbleBossBar = "RubbleBossBar";
    public const string RubbleName = "Rubble";
    // Menu cek power di lobby (LobbyHUD.LeftSideButtons, "Spinning" terlihat di research.md).
    public const string LobbyHud = "LobbyHUD";
    public const string MenuTrait = "Trait";
    public const string MenuSpin = "Spin";
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
    /// <summary>Terverifikasi RIE (Byzan, 23 Okt 2026), relatif PlayerGui. ScreenGui ini tidak memakai inset topbar (marker RIE dengan Inset 58 meleset ~58 px ke bawah).</summary>
    public const string AreasButtonPath = "LobbyHUD.LeftSideButtons.AreasButton";
    public const string StoryCardPath = "Finished UI.Areas.MainFrame.Content.Clipping.Container.MATCHES_Row1.Story.Story";
    public const string TeleportCardHint = "Click to Teleport";
    public const string StartCircleText = "Start Here";
    /// <summary>Terverifikasi RIE (Byzan, 23 Okt 2026): Workspace.Systems.QuqueRooms.&lt;n&gt;.JoinHitbox (Part 18×25×25, CanCollide false).</summary>
    public const string Systems = "Systems";
    public const string QueueRooms = "QuqueRooms"; // typo asli game
    public const string JoinHitbox = "JoinHitbox";
    public const string RoomGui = "RoomGui";
    /// <summary>RoomGui saat room sudah dibuat pemain lain: "Join Here" + "Players 3/4" + hitung mundur.</summary>
    public const string RoomOccupiedText = "Join Here";

    public const string LobbyModesList = "ModesList";   // [V]
    public const string LobbyMapsList = "MapsList";
    public const string LobbyActsList = "ActsList";
    public const string LobbyMapLine = "MapLine";       // .Tag = map terpilih
    public const string LobbyActLine = "ActLine";       // .Tag = act terpilih
    public const string LobbyStartButton = "StaerButton"; // (sic) ~(1364,934)
    // Terverifikasi snapshot RIE Byzan (23 Okt 2026, root "UI Animation (Finished)".Quque.MainFrame, AbsolutePosition 0,58):
    // ScreenGui ini TIDAK perlu inset topbar (AbsolutePosition sudah koordinat klien) → rect menu memakai inset lobby (0).
    // HUD skill (snapshot training Sukuna, 23 Okt 2026): PlayerGui.HUD.HUD.BottomLeftHolder.main.skills berisi tiga
    // ImageButton "skillBox" (urutan anak ≠ urutan tombol). Tombol = skillBox.keybind.Txt ("1"/"2"/"3");
    // cooldown = skillBox.Frame.cd.Txt (kosong = siap, angka = sisa detik, mis. "1.7"); terkunci = skillBox.Frame.MasteryLock Visible.
    public const string HudDashProgress = "HUD.HUD.BottomLeftHolder.buttons.Dash.Progress"; // Visible = Dash cooldown
    public const string HudSkills = "HUD.HUD.BottomLeftHolder.main.skills";
    /// <summary>v0.2.65: tombol Ultimate (G). Anak: HT, OverStroke, Progress.Frame1/Frame2.ProgressFram (+UIGradient, bar melingkar),
    /// Glow, FrameOutline, BGProgress, icon.left/right.icon; keybind.Txt = "G". v0.2.66: "Locked" tidak ada di template → siap = tidak diketahui.</summary>
    public const string HudUltimate = "HUD.HUD.BottomLeftHolder.UltimateFrame.Ultimate";
    public const string HudUltimateLock = "Locked";
    public const string HudSkillBox = "skillBox";
    public const string HudSkillKey = "keybind.Txt";
    public const string HudSkillCd = "Frame.cd.Txt";
    public const string HudSkillLock = "Frame.MasteryLock";

    // Layar tunggu setelah Start (snapshot S4 Byzan, 23 Okt 2026): PlayerGui."UI Animation (Finished)".Quque.StartNow
    // (ImageButton "Skip Waiting", ~(796,931)), .Sub "Uses 1 Crystal" (Visible = berbayar); Quque.Leave ~(1124,931). MainFrame Visible=false.
    public const string QueueRoot = "UI Animation (Finished).Quque";
    public const string QueueSkipButton = "StartNow";
    public const string QueueSkipCost = "Sub";
    public const string LobbyStartPath = "Content.RightHolder.Buttons.StaerButton.StaerButton";       // ImageButton
    public const string LobbyDifficultyContainer = "Content.RightHolder.RHS.Difficulty.Difficulty.Container"; // .<Easy|Medium|Hard|Nightmare>.<sama>.Hit
    public const string LobbyMapTitlePath = "Content.RightHolder.RHS.MapHolder.MapHolder.TxtHolder.MapLine.Title"; // "Hxh" / "ZZZ World"
    public const string LobbyDifficultyTagPath = "Content.RightHolder.RHS.MapHolder.MapHolder.TxtHolder.MapLine.Tag"; // "[EASY]" / "[HARD]"
    public const string LobbyActTagPath = "Content.RightHolder.RHS.MapHolder.MapHolder.TxtHolder.ActLine.Tag";   // "[Act 2]"
    public const string LobbyActsScroll = "Content.ActsList.List.ScrollingFrame"; // Act<n> (Frame, nama ganda per map; yang Visible = map terpilih) .Act<n> ImageButton .TxtHolder.Act "Act 1", .Locked
    public const string LobbyLeaveButton = "Leave";       // ~(1639,934)
    /// <summary>Resolusi acuan koordinat cadangan (Difficulty/Start) di atas; diskalakan ke ukuran klien jendela.</summary>
    public const double RefW = 1920, RefH = 1080;
    public const double StartRefX = 1364, StartRefY = 934;
    public static readonly (double X, double Y)[] DifficultyCenters = [(1033, 589), (1147, 589), (1261, 589), (1374, 589)];

    // SkillTree per unit
    public const string SkillTreeRoot = "UI Animation (Working).SkillTree";
    public const string SkillTreeTokens = "Header.Holder.Txt";
    public const string SlotCanBuy = "CanBuy";
    public const string BuyFrame = "BuyFrame";
    public const string BuyButton = "BuyButton";


    // ---- Mode non-Story (snapshot RIE Byzan, 24 Okt 2026) ----
    // Drop Target: kartu RewardN.RewardN.Main (.TxtShadow.Txt = nama, .TargetPill tampil hanya di Hard/Nightmare,
    // .TargetPill.Pill."Txt(Shadow)".Txt = "TARGET x3" / "UNTARGET" (= sedang ditarget), .TargetPill.Hit = tombol).
    public const string LobbyRewardsScroll = "Content.RightHolder.RHS.Rewards.Rewards.Container.ScrollingFrame";
    public const string LobbyRewardsTip = "Content.RightHolder.RHS.Rewards.Rewards.Header.TargetTip"; // "Tap a material to target it: x3 drop chance for it"
    // Mirror Blessing: FateBlessings.Picker (ScreenGui 0,58 → koordinat klien). OfferN.Body: Rarity.Txt, TextHolder.Heading/Value, PickButton.
    public const string BlessingPicker = "FateBlessings.Picker";
    // HUD raid: RunSide.Container.Title "Wave 1/5" / "Boss Wave", .Alive.Value "09", .NextBoss.Value "Wave 5"/"Now!".
    public const string RunSideContainer = "RunSide.Container";
    // BossBar: Header.Title "Evil Astx", Header.Tag "[IMMUNE]" (wave 1-4) → "[BOSS]" (wave boss), Header.Percent.
    public const string BossHeader = "BossBar.Container.Header";
    // Panel Trials: Tabs.RoomRewards (tab Floor Rewards), RoomRewards.CurrentRoom.Title "Floor 1",
    // RoomRewards.HighestRoom.Title "None yet", LeftButtons.Decrease/Increase, Buttons.StaerButton.StaerButton,
    // Rewards.Container.ScrollingFrame.RewardN.Main (TargetPill sama dengan lobby).
    public const string TrialsRoot = "UI Animation (Finished).Trials.MainFrame";
    // EndScreen: Holder.ConstraintedHolder.ButtonsHolder.{Retry,Next,Leave}Button.Button (Next hanya Story/Trials),
    // NextButton.MainText "Next Floor: 0/1". Drop: Holder.RewardsFrame.CutFrame.Holder.<money|gems|traitRerolls|luckySpins|materialNNN|unitX>.
    public const string EndButtonsHolder = "Holder.ConstraintedHolder.ButtonsHolder";
    public const string EndDropsHolder = "Holder.RewardsFrame.CutFrame.Holder";

    public static string DropLabel(string key) => key switch
    {
        "money" => "Coins",
        "gems" => "Gems",
        "traitRerolls" => "Trait Rerolls",
        "luckySpins" => "Lucky Spins",
        _ when key.StartsWith("unit", StringComparison.Ordinal) => "Unit " + key[4..],
        _ => key,
    };

    /// <summary>Icon kartu Rewards → nama material (dari snapshot Create Room/Trials). Ditambah otomatis saat menu terbaca.</summary>
    public static readonly IReadOnlyDictionary<string, string> MaterialIcons = new Dictionary<string, string>
    {
        ["rbxassetid://119252600098664"] = "Magic Pendant",
        ["rbxassetid://109913321338481"] = "Cursed Relic",
        ["rbxassetid://132677360974573"] = "Soul Fragment",
        ["rbxassetid://106984976173853"] = "Eyeball",
        ["rbxassetid://139900035439195"] = "Shinobi Scroll",
        ["rbxassetid://111269245025211"] = "Caprese",
        ["rbxassetid://138199229130273"] = "Ham",
        ["rbxassetid://78497914687710"] = "Omurice",
        ["rbxassetid://97090308752380"] = "Curse Ball",
        ["rbxassetid://99397651593848"] = "Forbidden Fruit",
        ["rbxassetid://111585695829783"] = "Pure Quill",
        ["rbxassetid://132113369360174"] = "Mana Feather",
        ["rbxassetid://102816679609155"] = "Fine Cloth",
        ["rbxassetid://118619358407255"] = "Pure Sword Shards",
    };

    /// <summary>Resolusi referensi koordinat fallback di atas (layar 1920x1080).</summary>
    public const double RefWidth = 1920, RefHeight = 1080;
}

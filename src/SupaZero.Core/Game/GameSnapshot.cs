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

public enum GameScreen { Unknown, Lobby, LobbyMenu, StartVote, InRound, Victory, Defeat, QueueWaiting, BlessingPick, TrialsMenu }

/// <summary>Status satu kotak skill di HUD: Key dari keybind.Txt; Remaining detik cooldown (null = siap); Locked = MasteryLock tampil.</summary>
/// <summary>Musuh di Workspace.enemies: posisi HumanoidRootPart + darah Humanoid.</summary>
public sealed record EnemyInfo(string Name, Settings.Vec3 Pos, double Health, double MaxHealth)
{
    public bool Alive => Health > 0;
}

/// <summary>Item di lantai ronde (Workspace.PooledDrops): Kind "cash" (koin) atau "health" (plus hijau). Dipungut dengan dilewati.</summary>
public sealed record PickupInfo(string Kind, Settings.Vec3 Pos)
{
    public bool IsHealth => Kind.StartsWith("health", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Kamera: posisi, LookVector, FieldOfView (vertikal, derajat), ViewportSize (= area klien).</summary>
public sealed record CameraInfo(Settings.Vec3 Pos, Settings.Vec3 Look, double Fov, double ViewW, double ViewH)
{
    /// <summary>Proyeksi titik dunia ke koordinat klien; null bila di belakang kamera. Asumsi kamera tanpa roll.</summary>
    public (double X, double Y)? WorldToScreen(Settings.Vec3 p)
    {
        double fl = Math.Sqrt(Look.X * Look.X + Look.Y * Look.Y + Look.Z * Look.Z);
        if (fl < 1e-6 || ViewW <= 0 || ViewH <= 0) return null;
        double fx = Look.X / fl, fy = Look.Y / fl, fz = Look.Z / fl;
        double rx = -fz, rz = fx, rl = Math.Sqrt(rx * rx + rz * rz); // right = look × up(0,1,0)
        if (rl < 1e-6) return null;
        rx /= rl; rz /= rl;
        double ux = -rz * fy, uy = rz * fx - rx * fz, uz = rx * fy; // up = right × look
        double dx = p.X - Pos.X, dy = p.Y - Pos.Y, dz = p.Z - Pos.Z;
        double z = dx * fx + dy * fy + dz * fz;
        if (z <= 0.1) return null;
        double x = dx * rx + dz * rz, y = dx * ux + dy * uy + dz * uz;
        double t = Math.Tan(Fov * Math.PI / 360), aspect = ViewW / ViewH;
        return ((1 + x / (z * t * aspect)) * ViewW / 2, (1 - y / (z * t)) * ViewH / 2);
    }
}

public sealed record SkillCooldown(string Key, double? Remaining, bool Locked)
{
    public bool Ready => Remaining is null && !Locked;
}

public sealed record SkillSlot(string Column, int Index, bool CanBuy, GuiRect? Rect);

/// <summary>Satu entri daftar (map/act/mode) di Create Lobby.</summary>
public sealed record LobbyEntry(string Name, bool Locked, bool Selected, GuiRect? Rect, string? Mode = null);

/// <summary>Satu bacaan keadaan game (read-only) per tick macro.</summary>
/// <summary>Satu room antrean (Workspace.Systems.QuqueRooms.&lt;n&gt;). Occupied = RoomGui menampilkan "Join Here"/"Players x/y" (sudah ada pemain lain).</summary>
public sealed record StartRoom(string Id, Settings.Vec3 Pos, bool Occupied, int? Players = null, int? MaxPlayers = null);

/// <summary>Kartu reward (Create Room / Trials). Targetable = pill TARGET tampil (Hard/Nightmare); Targeted = pill bertuliskan UNTARGET.</summary>
public sealed record RewardCard(string Name, bool Targetable, bool Targeted, GuiRect? Hit, string? Chance);

/// <summary>Kartu Mirror Blessing (FateBlessings.Picker.Holder.List.OfferN).</summary>
public sealed record BlessingOffer(string Name, string Rarity, string? Value, GuiRect? Pick);

/// <summary>Panel Trials (UI Animation (Finished).Trials.MainFrame). FloorTabOpen = tab Floor Rewards aktif.</summary>
public sealed record TrialsInfo(GuiRect? FloorTab, bool FloorTabOpen, int? SelectedFloor, int? HighestFloor,
    GuiRect? Decrease, GuiRect? Increase, GuiRect? Start, IReadOnlyList<RewardCard> Rewards, GuiRect? RewardsView);

/// <summary>Kotak Workspace.World.Areas.&lt;n&gt; (Part: pusat + Size).</summary>
public sealed record AreaBox(Settings.Vec3 Center, Settings.Vec3 Size)
{
    public bool Contains(Settings.Vec3 p, double margin = 0) =>
        Size.X > 0 && Math.Abs(p.X - Center.X) <= Size.X / 2 - margin && Math.Abs(p.Z - Center.Z) <= Size.Z / 2 - margin;
}

public sealed record GameSnapshot
{
    public bool Attached { get; init; }
    public long PlaceId { get; init; }
    public string LocalPlayer { get; init; } = string.Empty;

    // Karakter + kamera (untuk Navigator)
    public Settings.Vec3? PlayerPos { get; init; }
    /// <summary>Darah karakter 0..1 (Humanoid Health/MaxHealth), null bila tidak terbaca.</summary>
    public double? PlayerHp { get; init; }
    public IReadOnlyList<PickupInfo> Pickups { get; init; } = [];
    /// <summary>Peta navigasi ronde (Areas + rintangan Navigation) untuk cari jalur; null di luar ronde.</summary>
    public NavMap? Nav { get; init; }
    public (double X, double Z)? CameraLook { get; init; }
    public CameraInfo? Camera { get; init; }
    /// <summary>Musuh hidup/mati di Workspace.enemies (kosong bila tidak terbaca).</summary>
    public IReadOnlyList<EnemyInfo> Enemies { get; init; } = [];
    /// <summary>Rubble yang harus dihancurkan (juga dimasukkan ke Enemies sebagai "Rubble" bila tidak ada musuh hidup).</summary>
    public Settings.Vec3? RubblePos { get; init; }
    /// <summary>Diagnosa sumber posisi rubble (log).</summary>
    public string? RubbleInfo { get; init; }
    /// <summary>Posisi area berikutnya (Workspace.World.Spawns/Areas[Area+1]).</summary>
    public Settings.Vec3? NextAreaPos { get; init; }
    /// <summary>Pusat kotak World.Areas.&lt;TargetArea&gt; (dipakai untuk terus maju bila HUD "Enter area" belum hilang).</summary>
    public Settings.Vec3? NextAreaCenter { get; init; }

    // Lobby hub: tombol Areas (HUD kiri) → panel MATCHES "Click to Teleport" → dekat lingkaran "Start Here".
    public GuiRect? AreasButton { get; init; }
    /// <summary>Kartu teleport di panel Areas (Name = judul kartu, mis. "Story").</summary>
    public IReadOnlyList<LobbyEntry> TeleportCards { get; init; } = [];
    /// <summary>v0.2.49: tombol menu di LobbyHUD (Name = "Trait" / "Spin") untuk cek power + trait sebelum Create Room.</summary>
    public IReadOnlyList<LobbyEntry> HubMenuButtons { get; init; } = [];
    /// <summary>Layar Trait Reroll / daftar slot karakter (menu Spin) sedang tampil.</summary>
    public bool TraitMenuOpen { get; init; }
    public bool SpinMenuOpen { get; init; }
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
    /// <summary>Tombol "Skip Waiting" di layar tunggu room (Quque.StartNow), null bila tidak tampil.</summary>
    public GuiRect? QueueSkip { get; init; }
    /// <summary>Label "Uses 1 Crystal" di tombol Skip Waiting tampil → skip berbayar.</summary>
    public bool QueueSkipCosts { get; init; }
    public GuiRect? LobbyStart { get; init; }
    /// <summary>Area tampak ScrollingFrame daftar map/act/mode (untuk cek apakah entri perlu di-scroll dulu).</summary>
    public GuiRect? LobbyMapsView { get; init; }
    public GuiRect? LobbyActsView { get; init; }
    public GuiRect? LobbyModesView { get; init; }
    /// <summary>Tombol difficulty yang terbaca dari teks Easy/Medium/Hard/Nightmare di menu Create Room.</summary>
    public IReadOnlyList<LobbyEntry> LobbyDifficulties { get; init; } = [];

    /// <summary>Ukuran area klien jendela Roblox (diisi MacroRunner). 0 = tidak diketahui.</summary>
    public int ClientW { get; init; }
    /// <summary>Shift lock terdeteksi otomatis (kursor selalu kembali ke tengah jendela setelah digeser).</summary>
    public bool ShiftLock { get; init; }
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
    /// <summary>Drop di layar hasil ("Coins $14,746", "Magic Pendant x1"), dari EndScreen RewardsFrame.</summary>
    public IReadOnlyList<string> Drops { get; init; } = [];
    public GuiRect? RetryButton { get; init; }
    public GuiRect? LeaveButton { get; init; }

    /// <summary>Kotak skill HUD (HUD.HUD.BottomLeftHolder.main.skills.skillBox), kosong bila tidak terbaca.</summary>
    public IReadOnlyList<SkillCooldown> SkillCooldowns { get; init; } = [];
    /// <summary>v0.2.65: Ultimate siap (HUD UltimateFrame tampil, ikon Locked tidak). Null = HUD tidak terbaca.</summary>
    public bool? UltReady { get; init; }
    /// <summary>Dash sedang cooldown (HUD buttons.Dash.Progress tampil).</summary>
    public bool DashCooling { get; init; }

    // Upgrade
    public bool SkillTreeOpen { get; init; }
    public int? Tokens { get; init; }
    public IReadOnlyList<SkillSlot> Slots { get; init; } = [];
    public GuiRect? BuyButton { get; init; }

    // Power di loadout (lobby; di stage memakai hasil lobby terakhir)
    public IReadOnlyList<EquippedPower> Equipped { get; init; } = [];
    /// <summary>Power yang sedang dipakai (nama tampil), dari animasi karakter. null = belum terbaca.</summary>
    public string? ActivePower { get; init; }

    // Mode non-Story
    public IReadOnlyList<RewardCard> LobbyRewards { get; init; } = [];
    public GuiRect? LobbyRewardsView { get; init; }
    /// <summary>Teks tip Drop Target yang tampil ("... x3 drop chance ..."), null = difficulty tanpa bonus.</summary>
    public string? LobbyTargetTip { get; init; }
    public IReadOnlyList<BlessingOffer> Blessings { get; init; } = [];
    public TrialsInfo? Trials { get; init; }
    /// <summary>RunSide raid: "Wave 1/5" / "Boss Wave".</summary>
    public string? RunWave { get; init; }
    public int? RunAlive { get; init; }
    public string? BossName { get; init; }
    /// <summary>"IMMUNE" / "BOSS" dari BossBar.</summary>
    public string? BossTag { get; init; }
    public bool BossImmune => BossTag?.Contains("IMMUNE", StringComparison.OrdinalIgnoreCase) == true;
    public Settings.Vec3? BossSpawn { get; init; }
    public AreaBox? CurrentArea { get; init; }
    public string? NextButtonText { get; init; }

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
            // Vote hanya sebelum musuh muncul; ada musuh hidup = ronde berjalan (vote basi tidak boleh menahan Fight).
            if (StartVoteVisible && (EnemyCount <= 0 || (Enemies.Count > 0 && !Enemies.Any(e => e.Alive)))) return GameScreen.StartVote;
            if (Blessings.Count > 0) return GameScreen.BlessingPick;
            if (LobbyMenuOpen) return GameScreen.LobbyMenu;
            if (QueueSkip is not null) return GameScreen.QueueWaiting;
            if (Trials is not null) return GameScreen.TrialsMenu;
            if (InRound) return GameScreen.InRound;
            return GameScreen.Lobby;
        }
    }
}

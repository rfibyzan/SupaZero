namespace SupaZero.Core.Game;

/// <summary>GUI rect in Roblox window client coordinates (pixels).</summary>
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

/// <summary>State of one HUD skill box: Key from keybind.Txt; Remaining cooldown seconds (null = ready); Locked = MasteryLock shown.</summary>
/// <summary>Enemy in Workspace.enemies: HumanoidRootPart position + Humanoid HP.</summary>
public sealed record EnemyInfo(string Name, Settings.Vec3 Pos, double Health, double MaxHealth, int Highlights = 1)
{
    public bool Alive => Health > 0;
    /// <summary>
    /// v0.2.81: boss IMMUNE flag from the model. RIE snapshot Evil Reflections (Byzan, 24 Oct 23:52 vs 23:53): while [IMMUNE] the boss model
    /// has TWO Highlight children named "Highlight" (normal enemies have one); once attackable, the second Highlight disappears.
    /// </summary>
    public bool ImmuneMark => Highlights >= 2;
}

/// <summary>Item on the round floor (Workspace.PooledDrops): Kind "cash" (coin) or "health" (green plus). Picked up by walking over it.</summary>
public sealed record PickupInfo(string Kind, Settings.Vec3 Pos)
{
    public bool IsHealth => Kind.StartsWith("health", StringComparison.OrdinalIgnoreCase);
}

/// <summary>v0.2.67: corpse that can be raised (Shadow Lord): Workspace.AriseShadows.&lt;n&gt;.Arise. Hold = seconds to hold E.</summary>
public sealed record AriseInfo(string Name, Settings.Vec3 Pos, double Hold, double MaxDistance);
/// <summary>v0.3.18 Trials Lift (World.Markers.Lift ProximityPrompt "Ascend"). Enabled = the game turned the prompt on.</summary>
public sealed record LiftInfo(Settings.Vec3 Pos, bool Enabled, double MaxDistance, string Action);

/// <summary>Camera: position, LookVector, FieldOfView (vertical, degrees), ViewportSize (= client area).</summary>
public sealed record CameraInfo(Settings.Vec3 Pos, Settings.Vec3 Look, double Fov, double ViewW, double ViewH)
{
    /// <summary>v0.2.77: Camera.CameraSubject (full path, e.g. "Workspace.Astakun17.Humanoid"); null when the offset is missing.</summary>
    public string? Subject { get; init; }

    /// <summary>Projects a world point to client coordinates; null when behind the camera. Assumes a camera without roll.</summary>
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
    /// <summary>v0.2.79: definitely on cooldown/locked (unreadable cd text = NaN → neither ready nor on cooldown).</summary>
    public bool Cooling => Locked || Remaining is > 0;
}

/// <summary>SkillTree node. Index 0 = topmost (buy order top → bottom). Price null = already bought / unreadable.</summary>
public sealed record SkillSlot(string Column, int Index, bool CanBuy, GuiRect? Rect, int? Price = null, bool Selected = false, bool Locked = false);

/// <summary>One list entry (map/act/mode) in Create Lobby.</summary>
public sealed record LobbyEntry(string Name, bool Locked, bool Selected, GuiRect? Rect, string? Mode = null);

/// <summary>One read of the game state (read-only) per macro tick.</summary>
/// <summary>One queue room (Workspace.Systems.QuqueRooms.&lt;n&gt;). Occupied = RoomGui shows "Join Here"/"Players x/y" (another player is already in it).</summary>
public sealed record StartRoom(string Id, Settings.Vec3 Pos, bool Occupied, int? Players = null, int? MaxPlayers = null);

/// <summary>Reward card (Create Room / Trials). Targetable = TARGET pill shown (Hard/Nightmare); Targeted = pill reads UNTARGET.</summary>
public sealed record RewardCard(string Name, bool Targetable, bool Targeted, GuiRect? Hit, string? Chance);

/// <summary>Mirror Blessing card (FateBlessings.Picker.Holder.List.OfferN).</summary>
public sealed record BlessingOffer(string Name, string Rarity, string? Value, GuiRect? Pick, string? Description = null);

/// <summary>Panel Trials (UI Animation (Finished).Trials.MainFrame). FloorTabOpen = Floor Rewards tab active.</summary>
public sealed record TrialsInfo(GuiRect? FloorTab, bool FloorTabOpen, int? SelectedFloor, int? HighestFloor,
    GuiRect? Decrease, GuiRect? Increase, GuiRect? Start, IReadOnlyList<RewardCard> Rewards, GuiRect? RewardsView)
{
    /// <summary>v0.3.7: Buttons.LeaveButton.LeaveButton (text "Leave", s0819: 1617,911). Closes the Trials panel + room menu.</summary>
    public GuiRect? Leave { get; init; }
}

/// <summary>Workspace.World.Areas.&lt;n&gt; box (Part: center + Size).</summary>
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

    // Character + camera (for Navigator)
    public Settings.Vec3? PlayerPos { get; init; }
    /// <summary>Character HP 0..1 (Humanoid Health/MaxHealth), null when unreadable.</summary>
    public double? PlayerHp { get; init; }
    public IReadOnlyList<PickupInfo> Pickups { get; init; } = [];
    /// <summary>v0.2.67: active ARISE prompts (Shadow Lord), empty when none.</summary>
    public IReadOnlyList<AriseInfo> Arises { get; init; } = [];
    public LiftInfo? Lift { get; init; }
    /// <summary>v0.2.70: Shadows count / capacity from the System panel (null when unreadable).</summary>
    public (int Count, int Max)? Shadows { get; init; }
    /// <summary>Round navigation map (Areas + Navigation obstacles) for pathfinding; null outside a round.</summary>
    public NavMap? Nav { get; init; }
    public (double X, double Z)? CameraLook { get; init; }
    public CameraInfo? Camera { get; init; }

    /// <summary>
    /// v0.2.75: sideways camera offset relative to the character (studs, + = right). Roblox shift lock shifts the camera ±1.75 studs to the right of
    /// the character; without shift lock the camera looks straight at the character (≈0). The Anime Zero shift lock icon = Roblox default cursor,
    /// PlayerScripts/UserInputService/HUD.HUD.Cursor do not change (Byzan snapshot 24 Oct 23:18), so it is read from camera geometry.
    /// </summary>
    /// <summary>
    /// v0.2.77: the camera really follows the character: CameraSubject = player Humanoid/character (when readable) and camera distance ≤ 40 studs.
    /// false when an Ultimate animation/cutscene moves the camera; null when unreadable. When false, the camera-derived shift lock state is not used.
    /// </summary>
    /// <summary>Camera FieldOfView during normal play (Anime Zero/Roblox default).</summary>
    public const double NormalFov = 70;

    public bool? CameraFollowsPlayer
    {
        get
        {
            if (Camera is not { } c || PlayerPos is not { } p) return null;
            // v0.2.78: Byzan Watch 24 Oct 23:34: during the Ultimate animation FieldOfView jumps 11..120 (normally exactly 70) while the camera
            // stays ≤40 studs from the character → FOV outside 70±1 = camera controlled by a script.
            if (Math.Abs(c.Fov - NormalFov) > 1.0) return false;
            if (c.Subject is { } subj && !string.IsNullOrEmpty(LocalPlayer)
                && !(subj.EndsWith("." + LocalPlayer + ".Humanoid", StringComparison.Ordinal) || subj.EndsWith("." + LocalPlayer, StringComparison.Ordinal)
                     || subj.EndsWith("." + LocalPlayer + ".HumanoidRootPart", StringComparison.Ordinal)))
                return false;
            double dx = c.Pos.X - p.X, dy = c.Pos.Y - p.Y, dz = c.Pos.Z - p.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= 40;
        }
    }

    public double? CameraSideOffset
    {
        get
        {
            if (Camera is not { } c || PlayerPos is not { } p || CameraFollowsPlayer != true) return null;
            double lx = c.Look.X, lz = c.Look.Z, l = Math.Sqrt(lx * lx + lz * lz);
            if (l < 1e-3) return null;
            double rx = -lz / l, rz = lx / l;
            return (c.Pos.X - p.X) * rx + (c.Pos.Z - p.Z) * rz;
        }
    }

    /// <summary>Shift lock state from memory: true when the camera offset is 1..2.6 studs to the right, false when &lt; 0.5 studs, null when uncertain/unreadable.</summary>
    public bool? ShiftLockFromCamera => CameraSideOffset is not { } o ? null : o is >= 1.0 and <= 2.6 ? true : Math.Abs(o) < 0.5 ? false : null;
    /// <summary>Living/dead enemies in Workspace.enemies (empty when unreadable).</summary>
    public IReadOnlyList<EnemyInfo> Enemies { get; init; } = [];
    /// <summary>Rubble to destroy (also added to Enemies as "Rubble" when no living enemies).</summary>
    public Settings.Vec3? RubblePos { get; init; }
    /// <summary>v0.2.91: standing point for hitting the rubble = nearest point on Border*.Rubble.ClimbStop (null = unreadable).</summary>
    public Settings.Vec3? RubbleStand { get; init; }
    /// <summary>v0.3.12: RubbleBar Adornee rubble whose Blocker is still solid, HUD not mentioning rubble yet (can be hit once the wave is clear).</summary>
    public Settings.Vec3? RubblePending { get; init; }
    /// <summary>Rubble HP 0..1 from the RubbleBar text (null = unreadable).</summary>
    public double? RubbleHp { get; init; }
    /// <summary>Diagnostics for the rubble position source (log).</summary>
    public string? RubbleInfo { get; init; }
    /// <summary>Next area position (Workspace.World.Spawns/Areas[Area+1]).</summary>
    public Settings.Vec3? NextAreaPos { get; init; }
    /// <summary>Center of the World.Areas.&lt;TargetArea&gt; box (used to keep advancing while the HUD "Enter area" has not disappeared).</summary>
    public Settings.Vec3? NextAreaCenter { get; init; }

    // Lobby hub: Areas button (left HUD) → MATCHES panel "Click to Teleport" → near the "Start Here" circle.
    public GuiRect? AreasButton { get; init; }
    /// <summary>Teleport cards in the Areas panel (Name = card title, e.g. "Story").</summary>
    public IReadOnlyList<LobbyEntry> TeleportCards { get; init; } = [];
    /// <summary>v0.2.49: menu buttons in LobbyHUD (Name = "Trait" / "Spin") for checking power + trait before Create Room.</summary>
    public IReadOnlyList<LobbyEntry> HubMenuButtons { get; init; } = [];
    /// <summary>The Trait Reroll screen / character slot list (Spin menu) is showing.</summary>
    public bool TraitMenuOpen { get; init; }
    /// <summary>v0.3.6 (RIE Trait_Reroll 25 Oct 08:54): header CharacterSwitch.CharacterName.Txt ("Shadow Lord 3/5") and the ">" button (CharacterSwitch.Next.Next).</summary>
    public string? TraitHeader { get; init; }
    public GuiRect? TraitNext { get; init; }
    public bool SpinMenuOpen { get; init; }
    /// <summary>v0.3.12: LobbyHUD "Spinning" button (SummonButton) when visible.</summary>
    public GuiRect? SpinButton { get; init; }
    /// <summary>v0.3.12: Roll menu exit button (MainFrame.ExitButton.HitPad) when open.</summary>
    public GuiRect? RollExit { get; init; }
    /// <summary>Create Room circle detected: the part holding the "Start Here" text in Workspace.</summary>
    public IReadOnlyList<Settings.Vec3> StartCircles { get; init; } = [];
    /// <summary>Rooms from QuqueRooms.*.JoinHitbox + occupied state (re-read every tick).</summary>
    public IReadOnlyList<StartRoom> StartRooms { get; init; } = [];

    // Lobby
    public bool LobbyMenuOpen { get; init; }
    public IReadOnlyList<LobbyEntry> LobbyModes { get; init; } = [];
    public IReadOnlyList<LobbyEntry> LobbyMaps { get; init; } = [];
    public IReadOnlyList<LobbyEntry> LobbyActs { get; init; } = [];
    public string? LobbyMapTag { get; init; }
    public string? LobbyActTag { get; init; }
    /// <summary>Selected difficulty from MapLine.Tag ("[HARD]" → "Hard"), null when unreadable.</summary>
    public string? LobbyDifficultyTag { get; init; }
    /// <summary>"Skip Waiting" button on the room waiting screen (Quque.StartNow), null when not shown.</summary>
    public GuiRect? QueueSkip { get; init; }
    /// <summary>"Uses 1 Crystal" label shown on the Skip Waiting button → paid skip.</summary>
    public bool QueueSkipCosts { get; init; }
    public GuiRect? LobbyStart { get; init; }
    /// <summary>Create Room menu Leave button (MainFrame) = cancel the room.</summary>
    public GuiRect? LobbyLeave { get; init; }
    /// <summary>Calibration diagnostics: child names of LobbyHUD.LeftSideButtons (to find the actual Trait/Spin buttons).</summary>
    public IReadOnlyList<string> HubButtonNames { get; init; } = [];
    /// <summary>Visible area of the Areas panel (Content.Clipping) for scrolling to hidden cards.</summary>
    public GuiRect? AreasView { get; init; }
    /// <summary>Trait Reroll panel standing point (GuideSpots.Enchants) and exit point (AreasTeleportLocation.Traits).</summary>
    public Settings.Vec3? TraitSpot { get; init; }
    public Settings.Vec3? TraitExit { get; init; }
    /// <summary>Visible area of the map/act/mode list ScrollingFrame (to check whether an entry must be scrolled to first).</summary>
    public GuiRect? LobbyMapsView { get; init; }
    public GuiRect? LobbyActsView { get; init; }
    public GuiRect? LobbyModesView { get; init; }
    /// <summary>Difficulty buttons read from the Easy/Medium/Hard/Nightmare text in the Create Room menu.</summary>
    public IReadOnlyList<LobbyEntry> LobbyDifficulties { get; init; } = [];

    /// <summary>Roblox window client area size (filled in by MacroRunner). 0 = unknown.</summary>
    public int ClientW { get; init; }
    /// <summary>v0.2.79: Camera ViewportSize equals the Roblox window client area (±2 px). false when either is unreadable.</summary>
    public bool ViewportMatchesClient => Camera is { } c && ClientW > 0 && ClientH > 0
        && Math.Abs(c.ViewW - ClientW) <= 2 && Math.Abs(c.ViewH - ClientH) <= 2;
    /// <summary>Shift lock detected automatically (the cursor always returns to the window center after being moved).</summary>
    public bool ShiftLock { get; init; }
    public int ClientH { get; init; }

    // Round
    public bool StartVoteVisible { get; init; }
    public GuiRect? StartVoteYes { get; init; }
    public string? HudRaw { get; init; }
    public HudInfo? Hud { get; init; }
    /// <summary>Child count of Workspace.enemies; -1 = folder missing.</summary>
    public int EnemyCount { get; init; } = -1;
    public bool BossBarVisible { get; init; }

    // End screen
    public bool EndScreenVisible { get; init; }
    public string? ResultText { get; init; }
    public GuiRect? NextButton { get; init; }
    /// <summary>Drops on the result screen ("Coins $14,746", "Magic Pendant x1"), from EndScreen RewardsFrame.</summary>
    public IReadOnlyList<string> Drops { get; init; } = [];
    public GuiRect? RetryButton { get; init; }
    public GuiRect? LeaveButton { get; init; }

    /// <summary>HUD skill boxes (HUD.HUD.BottomLeftHolder.main.skills.skillBox), empty when unreadable.</summary>
    public IReadOnlyList<SkillCooldown> SkillCooldowns { get; init; } = [];
    /// <summary>v0.2.65: Ultimate ready (HUD UltimateFrame shown, Locked icon not). Null = HUD unreadable / no ready indicator (v0.2.66).</summary>
    public bool? UltReady { get; init; }
    /// <summary>Dash on cooldown (HUD buttons.Dash.Progress shown).</summary>
    /// <summary>v0.2.79: null = Dash button unreadable → Dash not pressed.</summary>
    public bool? DashCooling { get; init; }

    // Upgrade
    public bool SkillTreeOpen { get; init; }
    public int? Tokens { get; init; }
    public IReadOnlyList<SkillSlot> Slots { get; init; } = [];
    public GuiRect? BuyButton { get; init; }
    /// <summary>Slot number of the selected unit in SkillTree (Slot5 → 5), null = unreadable.</summary>
    public int? SkillUnitSlot { get; init; }
    /// <summary>SkillTree unit name (from Roll.CharacterInfo when the Roll selected slot = SkillTree slot), null = uncertain.</summary>
    public string? SkillUnitName { get; init; }
    /// <summary>v0.2.97: unit list at the bottom of SkillTree (Name = "SlotN", Locked = empty slot without a level, Rect = MainButton).</summary>
    public IReadOnlyList<LobbyEntry> SkillUnits { get; init; } = [];
    public string? SkillUnitDebug { get; init; }
    /// <summary>Visible area of the SkillTree unit list (CharacterSlots.Fade.List, horizontal ScrollingFrame).</summary>
    public GuiRect? SkillUnitView { get; init; }
    /// <summary>SkillTree X button (Header.ExitButton).</summary>
    public GuiRect? SkillTreeExit { get; init; }
    /// <summary>Units button in LobbyHUD (opens SkillTree).</summary>
    public GuiRect? UnitsButton { get; init; }

    // Power in the loadout (lobby; in a stage uses the last lobby reading)
    public IReadOnlyList<EquippedPower> Equipped { get; init; } = [];
    /// <summary>Power currently equipped (display name), from the character animation. null = not read yet.</summary>
    public string? ActivePower { get; init; }

    // Non-Story modes
    public IReadOnlyList<RewardCard> LobbyRewards { get; init; } = [];
    public GuiRect? LobbyRewardsView { get; init; }
    /// <summary>Drop Target tip text shown ("... x3 drop chance ..."), null = difficulty without bonus.</summary>
    public string? LobbyTargetTip { get; init; }
    public IReadOnlyList<BlessingOffer> Blessings { get; init; } = [];
    public TrialsInfo? Trials { get; init; }
    /// <summary>RunSide raid: "Wave 1/5" / "Boss Wave".</summary>
    public string? RunWave { get; init; }
    public int? RunAlive { get; init; }
    public string? BossName { get; init; }
    /// <summary>"IMMUNE" / "BOSS" from BossBar.</summary>
    public string? BossTag { get; init; }
    public bool BossImmune => BossTag?.Contains("IMMUNE", StringComparison.OrdinalIgnoreCase) == true;
    public Settings.Vec3? BossSpawn { get; init; }
    public AreaBox? CurrentArea { get; init; }
    public string? NextButtonText { get; init; }

    public bool InRound => Hud is not null || EnemyCount >= 0;

    public bool IsDefeat => ResultText?.Contains("DEFEAT", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Screen classification. Research note: on a loss EnemyCount still says "Story complete", so a win is only from EndScreen
    /// without ResultText DEFEAT.
    /// </summary>
    public GameScreen Screen
    {
        get
        {
            if (!Attached) return GameScreen.Unknown;
            if (EndScreenVisible || IsDefeat) return IsDefeat ? GameScreen.Defeat : GameScreen.Victory;
            // Vote only before enemies spawn; living enemies = round in progress (a stale vote must not block Fight).
            // v0.2.71: Workspace.Enemies can contain leftover models without HRP/HP (corpses, shadows) → EnemyCount > 0 but the enemy list is empty,
            // previously the vote was never clicked (Byzan screenshot 24 Oct 22:42, "Get ready" 0/1 votes). Enough: no living enemy read.
            if (StartVoteVisible && !Enemies.Any(e => e.Alive && e.Name != AnimeZeroPaths.RubbleName) && (EnemyCount <= 0 || Enemies.Count > 0 || HudRaw?.Contains("ready", StringComparison.OrdinalIgnoreCase) == true)) return GameScreen.StartVote;
            if (Blessings.Count > 0) return GameScreen.BlessingPick;
            if (LobbyMenuOpen) return GameScreen.LobbyMenu;
            if (QueueSkip is not null) return GameScreen.QueueWaiting;
            if (Trials is not null) return GameScreen.TrialsMenu;
            if (InRound) return GameScreen.InRound;
            return GameScreen.Lobby;
        }
    }
}

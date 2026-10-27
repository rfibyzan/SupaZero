using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

/// <summary>
/// Reads a <see cref="GameSnapshot"/> from Roblox memory (READ-ONLY, via the RIE reader). Never writes any memory.
/// GUI nodes whose exact path is not verified yet are searched by name (bounded BFS) and cached; the cache is re-validated
/// on every read (name + parent still match) because Roblox GUIs are often recreated.
/// </summary>
public sealed partial class GameReader(InstanceReader reader, PropertyReader props)
{
    private readonly Dictionary<string, long> _cache = new(StringComparer.Ordinal);

    public double GuiInsetY { get; set; } = ScreenMap.DefaultGuiInsetY;
    public double GuiScale { get; set; } = 1;
    /// <summary>Y inset for lobby hub GUIs (LobbyHUD / Finished UI). Verified 0: these ScreenGuis ignore the topbar.</summary>
    public double LobbyInsetY { get; set; } = 0;

    /// <summary>Create Room menu rect: ScreenGui without topbar inset (verified by snapshot, MainFrame AbsolutePosition 0,58).</summary>
    private GuiRect? MenuRect(long node) => HubRect(node);

    private string? CleanText(long node)
    {
        string? t = node != 0 ? Text(node) : null;
        if (t is null) return null;
        t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "").Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>
    /// v0.3.12b (PlayerGui snapshot 26 Oct 08:58:42/08:58:55): Roll.SurfaceGui.MainFrame uses SurfaceGui coordinates (origin 0,0),
    /// while ScreenGui Roll is at AbsolutePosition 0,58 → the Exit button (HitPad Y 864) is at Y 922 on screen. Without this offset
    /// the Exit click lands 58 px too high. When MainFrame is directly under the ScreenGui, offset 0.
    /// </summary>
    private double SurfaceShiftY(long frame)
    {
        long sg = reader.GetParent(frame);
        if (sg == 0 || reader.GetClassName(sg) != "SurfaceGui") return 0;
        long scr = reader.GetParent(sg);
        return AbsY(scr) is { } a && AbsY(sg) is { } b ? a - b : 0;
    }

    private double? AbsY(long node)
    {
        if (node == 0 || !props.Read(node).TryGetValue("AbsolutePosition", out var v) || v is null) return null;
        var parts = v.Split(',');
        return parts.Length >= 2 && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y) ? y : null;
    }

    public static double SurfaceToScreenY(double surfaceY, double screenGuiAbsY, double surfaceGuiAbsY) => surfaceY + (screenGuiAbsY - surfaceGuiAbsY);

    private GuiRect? HubRect(long node) =>
        Rect(node) is { } r ? r with { Y = r.Y + (LobbyInsetY - GuiInsetY) * GuiScale } : null;

    private IMemorySource M => reader.Memory;

    public GameSnapshot Read(DataModelRoots roots)
    {
        if (roots.DataModel == 0) return new GameSnapshot();
        long pg = roots.PlayerGui;
        long ws = roots.Workspace;

        string placeText = props.Read(roots.DataModel, "DataModel").TryGetValue("PlaceId", out var pid) ? pid : "0";
        long.TryParse(placeText, out long placeId);

        // Round HUD
        long hudText = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.EnemyCountPath) : 0;
        string? hudRaw = hudText != 0 && IsShown(hudText, pg) ? Text(hudText) : null;
        long enemies = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.Enemies) : 0;
        int enemyCount = enemies != 0 ? reader.GetChildren(enemies).Count : -1;

        // Start vote
        long vote = Find(pg, AnimeZeroPaths.StartVote);
        // ScreenGui StartVote can stay Enabled during a round (its inner frame is hidden) → the Yes button must be visible.
        // PlayerGui snapshot 24 Oct 2026: StartVote.Holder.Main.StartButton (ImageButton); "Yes" is only Text in StartButton.Txt(Shadow).Txt,
        // there is no node named "Yes" → previously never detected (Yes was not clicked).
        long voteYesNode = 0;
        if (vote != 0)
        {
            voteYesNode = reader.ResolvePath(vote, AnimeZeroPaths.StartVoteButtonPath);
            if (voteYesNode == 0) voteYesNode = Find(vote, AnimeZeroPaths.StartVoteButton);
            if (voteYesNode == 0) voteYesNode = Find(vote, AnimeZeroPaths.StartVoteYes);
        }
        bool voteShown = vote != 0 && IsShown(vote, pg) && voteYesNode != 0 && IsShown(voteYesNode, pg);
        // ScreenGui StartVote uses IgnoreGuiInset: StartButton AbsolutePosition (≈960,232) is already client coordinates.
        // With Inset 58 the click lands ~58 px below the button (Byzan screenshot 24 Oct 2026) → use the lobby inset (0).
        GuiRect? voteYes = voteShown ? HubRect(voteYesNode) : null;

        // End screen
        long end = Find(pg, AnimeZeroPaths.EndScreen);
        bool endShown = end != 0 && IsShown(end, pg);
        long result = endShown ? Find(end, AnimeZeroPaths.ResultText) : Find(pg, AnimeZeroPaths.ResultText);
        string? resultText = result != 0 && IsShown(result, pg) ? Text(result) : null;
        long boss = Find(pg, AnimeZeroPaths.BossBar);

        // Lobby
        long lobby = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.LobbyRoot) : 0;
        bool lobbyShown = lobby != 0 && IsShown(lobby, pg);
        long queue = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.QueueRoot) : 0;
        long qSkip = queue != 0 && !lobbyShown ? reader.FindChild(queue, AnimeZeroPaths.QueueSkipButton) : 0;
        bool qShown = qSkip != 0 && IsShown(qSkip, pg);
        long qCost = qShown ? reader.FindChild(qSkip, AnimeZeroPaths.QueueSkipCost) : 0;

        // SkillTree
        long tree = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.SkillTreeRoot) : 0;
        bool treeShown = tree != 0 && IsShown(tree, pg);

        var hud = HudText.Parse(hudRaw);
        // v0.2.49: "Break the rubble to continue!" → rubble becomes an attack target (Adornee of BillboardGui PlayerGui.RubbleBar).
        var playerPosEarly = ReadPlayerPos(roots.LocalPlayer);
        var rubbleAny = ReadRubble(pg, ws, hudRaw, playerPosEarly, voteShown, hud?.Enter == true);
        var rubble = _rubbleArmed ? rubbleAny : null;
        var rubblePending = _rubbleArmed ? null : rubbleAny;
        var enemyList = ReadEnemies(enemies);
        if (rubble is { } rb && !enemyList.Any(e => e.Alive))
        {
            enemyList.Add(new EnemyInfo(AnimeZeroPaths.RubbleName, rb, Math.Max(0.01, _rubbleHp ?? 1), 1));
            enemyCount = Math.Max(enemyCount, 0) + 1;
        }
        var playerPos = playerPosEarly;

        var snap = new GameSnapshot
        {
            Attached = true,
            PlayerPos = playerPos,
            CameraLook = ReadCameraLook(ws),
            Camera = ReadCamera(ws),
            Enemies = enemyList,
            RubblePos = rubble,
            RubbleStand = rubbleAny is not null ? _rubbleStand : null,
            RubblePending = rubblePending,
            RubbleHp = rubbleAny is not null ? _rubbleHp : null,
            RubbleInfo = _rubbleHp is { } rh ? $"{_rubbleInfo}; HP {rh:P0}" : _rubbleInfo,
            Pickups = ReadPickups(ws),
            Arises = ReadArises(ws),
            Lift = ReadLift(ws, hudRaw),
            Shadows = ParseShadows(pg != 0 && reader.ResolvePath(pg, AnimeZeroPaths.ShadowsValue) is var sv and not 0 ? Text(sv) : null),
            PlayerHp = ReadPlayerHp(roots.LocalPlayer),
            MyDamage = ReadMyDamage(pg, roots.LocalPlayer),
            FreeReviveShown = ReadFreeRevive(pg),
            NextAreaPos = hud is { TargetArea: > 0 } ? ReadAreaPos(ws, hud.TargetArea) : null,
            NextAreaCenter = hud is { TargetArea: > 0 } ? ReadAreaCenter(ws, hud.TargetArea) : null,
            LobbyModes = lobbyShown ? ReadList(Find(lobby, AnimeZeroPaths.LobbyModesList), pg) : [],
            LobbyMaps = lobbyShown ? ReadList(Find(lobby, AnimeZeroPaths.LobbyMapsList), pg) : [],
            LobbyActs = lobbyShown ? ReadActs(lobby, pg) : [],
            PlaceId = placeId,
            LocalPlayer = roots.LocalPlayer != 0 ? reader.GetName(roots.LocalPlayer) : string.Empty,
            HudRaw = hudRaw,
            Hud = hud,
            EnemyCount = enemyCount,
            StartVoteVisible = voteShown,
            StartVoteYes = voteYes,
            EndScreenVisible = endShown,
            ResultText = resultText,
            NextButton = endShown ? Rect(Find(end, AnimeZeroPaths.NextButton)) : null,
            RetryButton = endShown ? Rect(Find(end, AnimeZeroPaths.RetryButton)) : null,
            LeaveButton = endShown ? Rect(Find(end, AnimeZeroPaths.LeaveButton)) : null,
            BossBarVisible = boss != 0 && IsShown(boss, pg),
            LobbyMenuOpen = lobbyShown,
            QueueSkip = qShown ? MenuRect(qSkip) : null,
            SkillCooldowns = ReadSkillCooldowns(pg),
            UltReady = ReadUltReady(pg),
            DashCooling = ReadDashCooling(pg),
            QueueSkipCosts = qCost != 0 && IsShown(qCost, pg),
            LobbyMapTag = lobbyShown ? CleanText(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyMapTitlePath)) ?? Tag(Find(lobby, AnimeZeroPaths.LobbyMapLine)) : null,
            LobbyActTag = lobbyShown ? CleanText(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyActTagPath)) ?? Tag(Find(lobby, AnimeZeroPaths.LobbyActLine)) : null,
            LobbyDifficultyTag = lobbyShown ? CleanText(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyDifficultyTagPath))?.Trim('[', ']', ' ') : null,
            LobbyLeave = lobbyShown ? MenuRect(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyLeavePath)) : null,
            LobbyStart = lobbyShown ? MenuRect(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyStartPath)) ?? MenuRect(Find(lobby, AnimeZeroPaths.LobbyStartButton)) : null,
            LobbyMapsView = lobbyShown ? ListView(Find(lobby, AnimeZeroPaths.LobbyMapsList)) : null,
            LobbyActsView = lobbyShown ? ListView(Find(lobby, AnimeZeroPaths.LobbyActsList)) : null,
            LobbyModesView = lobbyShown ? ListView(Find(lobby, AnimeZeroPaths.LobbyModesList)) : null,
            LobbyDifficulties = lobbyShown ? ReadDifficulties(lobby, pg) : [],
            SkillTreeOpen = treeShown,
            Tokens = treeShown ? ParseInt(Text(reader.ResolvePath(tree, AnimeZeroPaths.SkillTreeTokens))) : null,
            Slots = treeShown ? ReadSlots(tree, pg) : [],
            BuyButton = treeShown && reader.FindChild(tree, AnimeZeroPaths.BuyFrame) is var bf and not 0 && IsShown(bf, pg)
                && reader.FindChild(bf, AnimeZeroPaths.BuyButton) is var bb and not 0 && IsShown(bb, pg) ? HubRect(bb) : null,
            SkillUnitSlot = treeShown ? SelectedSlot(tree, AnimeZeroPaths.SkillUnitList, AnimeZeroPaths.SkillUnitSelected) : null,
            SkillUnitName = treeShown ? SkillUnitName(tree, pg) : ResetRollTitle(),
            SkillUnitDebug = treeShown ? SkillUnitDebug : null,
            SkillUnits = treeShown ? ReadSkillUnits(tree) : [],
            SkillUnitView = treeShown && reader.ResolvePath(tree, AnimeZeroPaths.SkillUnitList) is var ul and not 0 ? HubRect(ul) : null,
            SkillTreeExit = treeShown && reader.ResolvePath(tree, AnimeZeroPaths.SkillTreeExit) is var ex and not 0 && IsShown(ex, pg) ? HubRect(ex) : null,
            UnitsButton = pg != 0 && reader.ResolvePath(pg, AnimeZeroPaths.UnitsButtonPath) is var ub and not 0 && IsShown(ub, pg) ? HubRect(ub) : null,
        };
        snap = ReadCodes(snap, pg);
        snap = ReadModes(snap, pg, ws, lobby, lobbyShown, end, endShown);
        if (snap.Screen != GameScreen.Lobby) return snap;
        var (areas, cards) = ScanHub(pg);
        var startRooms = ReadRooms(ws);
        long traitRoot = reader.ResolvePath(pg, TraitRoot), invList = RollPath(pg, InventoryList), rollFrame = RollFrame(pg);
        return snap with
        {
            AreasButton = areas, TeleportCards = cards, StartRooms = startRooms, StartCircles = ScanCircles(ws),
            HubMenuButtons = ScanMenuButtons(pg),
            AreasView = _areasView,
            TraitSpot = WsPos(ws, AnimeZeroPaths.TraitSpotPath),
            TraitExit = WsPos(ws, AnimeZeroPaths.TraitExitPath),
            HubButtonNames = pg != 0 && reader.ResolvePath(pg, "LobbyHUD.LeftSideButtons") is var lsb and not 0
                ? reader.GetChildren(lsb, 64).Select(c => reader.GetName(c)).Where(n => n.Length > 0).ToList() : [],
            TraitMenuOpen = traitRoot != 0 && IsShown(traitRoot, pg),
            TraitHeader = traitRoot != 0 && IsShown(traitRoot, pg) ? Plain(reader.ResolvePath(traitRoot, TraitPowerName)) : null,
            TraitNext = traitRoot != 0 && IsShown(traitRoot, pg) && reader.ResolvePath(traitRoot, AnimeZeroPaths.TraitNextButton) is var tn and not 0 && IsShown(tn, pg)
                && CleanText(reader.ResolvePath(tn, "Txt(Shadow).Txt"))?.Trim() == ">" ? HubRect(tn) : null,
            SpinMenuOpen = (invList != 0 && IsShown(invList, pg)) || (rollFrame != 0 && IsShown(rollFrame, pg)),
            SpinButton = SpinButtonNode(pg) is var sb and not 0 && IsShown(sb, pg) && HubRect(sb) is { W: > 4, H: > 4 } sbr ? sbr : null,
            RollExit = rollFrame != 0 && IsShown(rollFrame, pg) && reader.ResolvePath(rollFrame, AnimeZeroPaths.RollExit) is var rx and not 0 && IsShown(rx, pg)
                && HubRect(rx) is { } rxr ? rxr with { Y = rxr.Y + SurfaceShiftY(rollFrame) * GuiScale } : null,
        };
    }

    /// <summary>v0.3.41: new path (WideButtons) first, the old path (snapshot s0819) as fallback.</summary>
    private long SpinButtonNode(long pg) => pg == 0 ? 0
        : reader.ResolvePath(pg, AnimeZeroPaths.SpinButtonPath) is var n and not 0 ? n : reader.ResolvePath(pg, AnimeZeroPaths.SpinButtonPathOld);


    private long _hubAt;
    private bool HasShownDescendant(long node, long pg, string nameFragment, int depth)
    {
        if (depth >= 3) return false;
        foreach (long c in reader.GetChildren(node, 32))
        {
            if (reader.GetName(c).Contains(nameFragment, StringComparison.OrdinalIgnoreCase) && IsShown(c, pg)) return true;
            if (HasShownDescendant(c, pg, nameFragment, depth + 1)) return true;
        }
        return false;
    }

    /// <summary>Find a descendant named <paramref name="name"/> (BFS, max 9 levels / 6000 nodes), cached.</summary>
    public long Find(long root, string name, int maxDepth = 9, int maxNodes = 6000)
    {
        if (root == 0) return 0;
        string key = root.ToString("X") + "|" + name;
        if (_cache.TryGetValue(key, out long hit) && IsDescendantNamed(hit, root, name)) return hit;
        _cache.Remove(key);
        var q = new Queue<(long Node, int Depth)>();
        q.Enqueue((root, 0));
        int seen = 0;
        while (q.Count > 0 && seen++ < maxNodes)
        {
            var (node, depth) = q.Dequeue();
            if (node != root && reader.GetName(node).Equals(name, StringComparison.Ordinal))
            {
                _cache[key] = node;
                return node;
            }
            if (depth >= maxDepth) continue;
            foreach (long c in reader.GetChildren(node, 512)) q.Enqueue((c, depth + 1));
        }
        return 0;
    }

    private bool IsDescendantNamed(long node, long root, string name)
    {
        if (!InstanceReader.IsValid(node) || !reader.GetName(node).Equals(name, StringComparison.Ordinal)) return false;
        long cur = reader.GetParent(node);
        for (int i = 0; i < 32 && cur != 0; i++)
        {
            if (cur == root) return true;
            cur = reader.GetParent(cur);
        }
        return false;
    }

    /// <summary>Visible = Visible on itself and all GuiObject ancestors, and its ScreenGui is Enabled.</summary>
    public bool IsShown(long node, long stopAt)
    {
        long cur = node;
        for (int i = 0; i < 40 && InstanceReader.IsValid(cur) && cur != stopAt; i++)
        {
            string cls = reader.GetClassName(cur);
            if (PropertyReader.IsGuiObject(cls))
            {
                if (reader.Offsets.TryResolve("GuiObject.Visible", out long vo) && M.TryReadByte(cur + vo, out byte v) && v == 0)
                    return false;
            }
            else if (cls == "ScreenGui")
            {
                if (reader.Offsets.TryResolve("GuiObject.ScreenGui_Enabled", out long eo) && M.TryReadByte(cur + eo, out byte e) && e == 0)
                    return false;
                return true;
            }
            cur = reader.GetParent(cur);
        }
        return true;
    }

    public string? Text(long node)
    {
        if (node == 0 || !reader.Offsets.TryResolve("GuiObject.Text", out long to)) return null;
        string s = RobloxString.ReadStringAt(M, node + to, reader.StringLengthOffset, true);
        return s.Length == 0 ? null : s;
    }

    /// <summary>Value of the "Tag" child (StringValue) or Text, for MapLine/ActLine [V].</summary>
    private string? Tag(long node)
    {
        if (node == 0) return null;
        long tag = reader.FindChild(node, "Tag");
        if (tag != 0 && props.Read(tag).TryGetValue("Value", out var v) && v.Length > 0) return v;
        return Text(node);
    }

    public GuiRect? Rect(long node)
    {
        if (node == 0) return null;
        var p = props.Read(node);
        if (!ScreenMap.TryFromProperties(p, GuiInsetY, GuiScale, out var r)) return null;
        var g = new GuiRect(r.X, r.Y, r.Width, r.Height);
        return g.IsEmpty ? null : g;
    }

    private GuiRect? VisibleRect(long node, long pg) => node != 0 && IsShown(node, pg) ? Rect(node) : null;

    private static int? ParseInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) ? n : null;
    }
}

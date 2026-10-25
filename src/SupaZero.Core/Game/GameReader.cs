using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

/// <summary>
/// Membaca <see cref="GameSnapshot"/> dari memori Roblox (READ-ONLY, lewat reader RIE). Tidak menulis memori apa pun.
/// Node GUI yang path persisnya belum diverifikasi dicari per nama (BFS terbatas) lalu di-cache; cache divalidasi
/// ulang tiap baca (nama + parent masih cocok) karena GUI Roblox sering dibuat ulang.
/// </summary>
public sealed partial class GameReader(InstanceReader reader, PropertyReader props)
{
    private readonly Dictionary<string, long> _cache = new(StringComparer.Ordinal);

    public double GuiInsetY { get; set; } = ScreenMap.DefaultGuiInsetY;
    public double GuiScale { get; set; } = 1;
    /// <summary>Inset Y untuk GUI hub lobby (LobbyHUD / Finished UI). Terverifikasi 0: ScreenGui ini mengabaikan topbar.</summary>
    public double LobbyInsetY { get; set; } = 0;

    /// <summary>Rect menu Create Room: ScreenGui tanpa inset topbar (terverifikasi snapshot, MainFrame AbsolutePosition 0,58).</summary>
    private GuiRect? MenuRect(long node) => HubRect(node);

    private string? CleanText(long node)
    {
        string? t = node != 0 ? Text(node) : null;
        if (t is null) return null;
        t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "").Trim();
        return t.Length == 0 ? null : t;
    }

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

        // HUD ronde
        long hudText = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.EnemyCountPath) : 0;
        string? hudRaw = hudText != 0 && IsShown(hudText, pg) ? Text(hudText) : null;
        long enemies = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.Enemies) : 0;
        int enemyCount = enemies != 0 ? reader.GetChildren(enemies).Count : -1;

        // Vote mulai
        long vote = Find(pg, AnimeZeroPaths.StartVote);
        // ScreenGui StartVote bisa tetap Enabled selama ronde (frame di dalamnya disembunyikan) → wajib tombol Yes terlihat.
        // Snapshot PlayerGui 24 Okt 2026: StartVote.Holder.Main.StartButton (ImageButton); "Yes" hanya Text di StartButton.Txt(Shadow).Txt,
        // tidak ada node bernama "Yes" → dulu tidak pernah terdeteksi (Yes tidak diklik).
        long voteYesNode = 0;
        if (vote != 0)
        {
            voteYesNode = reader.ResolvePath(vote, AnimeZeroPaths.StartVoteButtonPath);
            if (voteYesNode == 0) voteYesNode = Find(vote, AnimeZeroPaths.StartVoteButton);
            if (voteYesNode == 0) voteYesNode = Find(vote, AnimeZeroPaths.StartVoteYes);
        }
        bool voteShown = vote != 0 && IsShown(vote, pg) && voteYesNode != 0 && IsShown(voteYesNode, pg);
        // ScreenGui StartVote memakai IgnoreGuiInset: AbsolutePosition StartButton (≈960,232) sudah koordinat klien.
        // Dengan Inset 58 klik jatuh ~58 px di bawah tombol (screenshot Byzan 24 Okt 2026) → pakai inset lobby (0).
        GuiRect? voteYes = voteShown ? HubRect(voteYesNode) : null;

        // Layar akhir
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
        // v0.2.49: "Break the rubble to continue!" → rubble jadi target serang (Adornee BillboardGui PlayerGui.RubbleBar).
        var playerPosEarly = ReadPlayerPos(roots.LocalPlayer);
        var rubble = ReadRubble(pg, ws, hudRaw, playerPosEarly, voteShown, hud?.Enter == true);
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
            RubbleStand = rubble is not null ? _rubbleStand : null,
            RubbleInfo = _rubbleHp is { } rh ? $"{_rubbleInfo}; nyawa {rh:P0}" : _rubbleInfo,
            Pickups = ReadPickups(ws),
            Arises = ReadArises(ws),
            Shadows = ParseShadows(pg != 0 && reader.ResolvePath(pg, AnimeZeroPaths.ShadowsValue) is var sv and not 0 ? Text(sv) : null),
            PlayerHp = ReadPlayerHp(roots.LocalPlayer),
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
            SkillUnitName = treeShown ? SkillUnitName(tree, pg) : null,
            SkillTreeExit = treeShown && reader.ResolvePath(tree, AnimeZeroPaths.SkillTreeExit) is var ex and not 0 && IsShown(ex, pg) ? HubRect(ex) : null,
            UnitsButton = pg != 0 && reader.ResolvePath(pg, AnimeZeroPaths.UnitsButtonPath) is var ub and not 0 && IsShown(ub, pg) ? HubRect(ub) : null,
        };
        snap = ReadModes(snap, pg, ws, lobby, lobbyShown, end, endShown);
        if (snap.Screen != GameScreen.Lobby) return snap;
        var (areas, cards) = ScanHub(pg);
        var startRooms = ReadRooms(ws);
        long traitRoot = reader.ResolvePath(pg, TraitRoot), invList = reader.ResolvePath(pg, InventoryList);
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
            SpinMenuOpen = invList != 0 && IsShown(invList, pg),
        };
    }

    // ---------- Hub lobby ----------

    private long _hubAt;
    private (GuiRect?, IReadOnlyList<LobbyEntry>) _hub = (null, []);
    private long _circlesWs, _circlesAt;
    private IReadOnlyList<Settings.Vec3> _circles = [];

    /// <summary>Path RIE manual (opsional) untuk tombol Areas / kartu Story; menimpa deteksi teks. Boleh "game.Players.X.PlayerGui...." atau relatif PlayerGui.</summary>
    public string? AreasButtonPath { get; set; }
    public string? StoryCardPath { get; set; }

    /// <summary>Path (relatif PlayerGui) node yang terakhir dipilih sebagai tombol Areas / kartu teleport, untuk log & dicocokkan di RIE.</summary>
    public string? LastAreasPick { get; private set; }
    public string? LastCardPick { get; private set; }

    /// <summary>Tombol "Areas" + kartu "Click to Teleport" di PlayerGui (scan teks terlihat, maks tiap 700 ms).</summary>
    private (GuiRect?, IReadOnlyList<LobbyEntry>) ScanHub(long pg)
    {
        long now = Environment.TickCount64;
        if (now - _hubAt < 250) return _hub;
        _hubAt = now;
        GuiRect? areas = null;
        var cards = new List<LobbyEntry>();

        // 1) path manual dari Settings, 2) path terverifikasi RIE, 3) scan teks.
        bool manualA = !string.IsNullOrWhiteSpace(AreasButtonPath);
        long manualAreas = ResolveGuiPath(pg, manualA ? AreasButtonPath : AnimeZeroPaths.AreasButtonPath);
        if (manualAreas != 0 && IsShown(manualAreas, pg)) { areas = HubRect(manualAreas); LastAreasPick = GuiPath(manualAreas, pg) + (manualA ? " (manual)" : " (path)"); }
        bool manualS = !string.IsNullOrWhiteSpace(StoryCardPath);
        long manualStory = ResolveGuiPath(pg, manualS ? StoryCardPath : AnimeZeroPaths.StoryCardPath);
        if (manualStory != 0 && IsShown(manualStory, pg) && HubRect(manualStory) is { } ms)
        {
            cards.Add(new LobbyEntry(nameof(Settings.FarmMode.Story), false, false, ms));
            LastCardPick = GuiPath(manualStory, pg) + (manualS ? " (manual)" : " (path)");
        }
        else manualStory = 0;

        bool haveStory = manualStory != 0;
        bool areasPathExists = manualAreas != 0; // tombol ada (mungkin tertutup panel Areas yang sedang terbuka)
        foreach (var (node, text) in (haveStory || areasPathExists) ? [] : VisibleTexts(pg, 5000))
        {
            if (areas is null && text.Trim().Equals(AnimeZeroPaths.AreasButtonText, StringComparison.OrdinalIgnoreCase))
            {
                long btn = ButtonFor(node);
                areas = HubRect(btn);
                LastAreasPick = GuiPath(btn, pg);
            }
            else if (text.Contains(AnimeZeroPaths.TeleportCardHint, StringComparison.OrdinalIgnoreCase))
            {
                string? title = CardTitle(node);
                if (title is null || (manualStory != 0 && title.Contains("Story", StringComparison.OrdinalIgnoreCase))) continue;
                long card = ButtonFor(node);
                if (HubRect(card) is not { } cr) continue;
                cards.Add(new LobbyEntry(title.Trim(), false, false, cr));
                if (title.Contains("Story", StringComparison.OrdinalIgnoreCase)) LastCardPick = GuiPath(card, pg);
            }
        }
        // v0.2.85: kartu lain di panel Areas (mis. "Traits" untuk kalibrasi power) → scan teks hanya di dalam Container panel.
        long clip = ResolveGuiPath(pg, AnimeZeroPaths.AreasClipping);
        long cont = clip != 0 ? reader.FindChild(clip, "Container") : 0;
        _areasView = clip != 0 && IsShown(clip, pg) ? HubRect(clip) : null;
        if (cont != 0 && IsShown(cont, pg))
            foreach (var (node, text) in VisibleTexts(cont, 1500))
            {
                if (!text.Contains(AnimeZeroPaths.TeleportCardHint, StringComparison.OrdinalIgnoreCase)) continue;
                if (CardTitle(node) is not { } title || cards.Any(c => c.Name.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                if (HubRect(ButtonFor(node)) is { } cr) cards.Add(new LobbyEntry(title.Trim(), false, false, cr));
            }
        return _hub = (areas, cards);
    }

    private GuiRect? _areasView;

    private long _menuAt;
    private IReadOnlyList<LobbyEntry> _menuButtons = [];

    /// <summary>
    /// Tombol menu Trait / Spin di PlayerGui.LobbyHUD (nama node atau teks terlihat mengandung "trait" / "spin" / "roll").
    /// Hanya LobbyHUD (tombol navigasi kiri), tidak pernah isi menu Spin/Trait (tombol roll di sana memakai Spins/Rerolls).
    /// </summary>
    private IReadOnlyList<LobbyEntry> ScanMenuButtons(long pg)
    {
        long now = Environment.TickCount64;
        if (now - _menuAt < 1000) return _menuButtons;
        _menuAt = now;
        var found = new Dictionary<string, LobbyEntry>(StringComparer.Ordinal);
        long hud = pg != 0 ? reader.FindChild(pg, AnimeZeroPaths.LobbyHud) : 0;
        if (hud == 0 || !IsShown(hud, pg)) return _menuButtons = [];
        var q = new Queue<(long Node, int Depth)>();
        q.Enqueue((hud, 0));
        int seen = 0;
        while (q.Count > 0 && seen++ < 800)
        {
            var (n, depth) = q.Dequeue();
            if (n != hud)
            {
                string name = reader.GetName(n);
                string? text = Plain(n);
                string? key = MenuKey(name) ?? MenuKey(text);
                if (key is not null && !found.ContainsKey(key) && IsShown(n, pg))
                {
                    long btn = IsButton(reader.GetClassName(n)) ? n : ButtonFor(n);
                    if (HubRect(btn) is { W: > 4, H: > 4 } r) found[key] = new LobbyEntry(key, false, false, r);
                }
            }
            if (depth < 5) foreach (long c in reader.GetChildren(n, 64)) q.Enqueue((c, depth + 1));
        }
        return _menuButtons = found.Values.ToList();
    }

    private static string? MenuKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (s.Contains("trait", StringComparison.OrdinalIgnoreCase)) return AnimeZeroPaths.MenuTrait;
        if (s.Contains("spin", StringComparison.OrdinalIgnoreCase) || s.Contains("roll", StringComparison.OrdinalIgnoreCase)) return AnimeZeroPaths.MenuSpin;
        return null;
    }

    private long ResolveGuiPath(long pg, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || pg == 0) return 0;
        path = path.Trim();
        int i = path.IndexOf(".PlayerGui.", StringComparison.OrdinalIgnoreCase);
        if (i >= 0) path = path[(i + ".PlayerGui.".Length)..];
        return reader.ResolvePath(pg, path);
    }

    private string GuiPath(long node, long pg)
    {
        var parts = new List<string>();
        for (long cur = node; cur != 0 && cur != pg && parts.Count < 16; cur = reader.GetParent(cur)) parts.Add(reader.GetName(cur));
        parts.Reverse();
        return "PlayerGui." + string.Join('.', parts);
    }

    private static bool IsButton(string cls) => cls is "ImageButton" or "TextButton";

    private static bool Contains(GuiRect r, double x, double y, double pad = 0) =>
        x >= r.X - pad && x <= r.X + r.W + pad && y >= r.Y - pad && y <= r.Y + r.H + pad;

    /// <summary>
    /// Tombol untuk sebuah label teks. Urutan: label itu sendiri bila tombol → ancestor tombol (maks 4 level) yang
    /// menutupi label dan tidak kebesaran → tombol terdekat di sekitar label (saudara / sepupu, mis. ikon di atas teks)
    /// → rect label itu sendiri. TIDAK pernah memakai frame wadah (dulu bisa jatuh ke tengah sidebar = tombol lain).
    /// </summary>
    private long ButtonFor(long label)
    {
        if (IsButton(reader.GetClassName(label))) return label;
        var lr = Rect(label);
        if (lr is null) return label;
        double cx = lr.Value.CenterX, cy = lr.Value.CenterY;
        double maxArea = Math.Max(lr.Value.W * lr.Value.H * 40, 160 * 160);

        long cur = reader.GetParent(label);
        for (int i = 0; i < 4 && cur != 0; i++, cur = reader.GetParent(cur))
        {
            if (!IsButton(reader.GetClassName(cur))) continue;
            if (Rect(cur) is { } r && Contains(r, cx, cy, 4) && r.W * r.H <= maxArea) return cur;
        }

        // Tombol di sekitar: cek keturunan induk & kakek (maks 2 level ke bawah), ambil yang paling dekat.
        long best = 0;
        double bestD = Math.Max(lr.Value.H * 2.5, 40);
        long scope = reader.GetParent(label);
        for (int up = 0; up < 2 && scope != 0; up++, scope = reader.GetParent(scope))
        {
            foreach (long c in Descendants(scope, 2))
            {
                if (c == label || !IsButton(reader.GetClassName(c)) || Rect(c) is not { } r) continue;
                if (r.W * r.H > maxArea) continue;
                double d = Contains(r, cx, cy) ? 0 : Math.Sqrt(Math.Pow(r.CenterX - cx, 2) + Math.Pow(r.CenterY - cy, 2));
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best != 0) return best;
        }
        return label;
    }

    private IEnumerable<long> Descendants(long root, int depth)
    {
        foreach (long c in reader.GetChildren(root, 64))
        {
            yield return c;
            if (depth > 1) foreach (long d in Descendants(c, depth - 1)) yield return d;
        }
    }

    /// <summary>Judul kartu teleport: teks lain di kartu yang sama (cari di induk lalu kakek).</summary>
    private string? CardTitle(long hint)
    {
        long scope = reader.GetParent(hint);
        for (int up = 0; up < 3 && scope != 0; up++, scope = reader.GetParent(scope))
            if (SiblingTitle(scope, hint) is { } t) return t;
        return null;
    }

    /// <summary>BFS PlayerGui yang melewati ScreenGui mati / GuiObject tak terlihat; hasil = (node teks, isi teks).</summary>
    private IEnumerable<(long Node, string Text)> VisibleTexts(long root, int maxNodes)
    {
        if (root == 0) yield break;
        var q = new Queue<long>();
        q.Enqueue(root);
        int seen = 0;
        while (q.Count > 0 && seen++ < maxNodes)
        {
            long node = q.Dequeue();
            string cls = reader.GetClassName(node);
            if (cls == "ScreenGui" && reader.Offsets.TryResolve("GuiObject.ScreenGui_Enabled", out long eo) && M.TryReadByte(node + eo, out byte e) && e == 0) continue;
            if (PropertyReader.IsGuiObject(cls))
            {
                if (reader.Offsets.TryResolve("GuiObject.Visible", out long vo) && M.TryReadByte(node + vo, out byte v) && v == 0) continue;
                if (PropertyReader.IsTextClass(cls) && Text(node) is { Length: > 0 } t) yield return (node, t);
            }
            foreach (long c in reader.GetChildren(node, 256)) q.Enqueue(c);
        }
    }

    /// <summary>Naik maks 4 level ke ImageButton/TextButton; tidak ada → induk langsung (kartu) atau node itu.</summary>
    private long ClickableAncestor(long node)
    {
        long cur = node;
        for (int i = 0; i < 4 && cur != 0; i++)
        {
            string cls = reader.GetClassName(cur);
            if (cls is "ImageButton" or "TextButton") return cur;
            cur = reader.GetParent(cur);
        }
        long parent = reader.GetParent(node);
        return parent != 0 && PropertyReader.IsGuiObject(reader.GetClassName(parent)) ? parent : node;
    }

    /// <summary>Teks lain di bawah induk yang sama (judul kartu, mis. "Story").</summary>
    private string? SiblingTitle(long parent, long except)
    {
        if (parent == 0) return null;
        foreach (long c in reader.GetChildren(parent, 64))
        {
            if (c == except) continue;
            if (FirstText(c, 0) is { } t && !t.Contains(AnimeZeroPaths.TeleportCardHint, StringComparison.OrdinalIgnoreCase)) return t;
        }
        return null;
    }

    /// <summary>
    /// Lingkaran Create Room: Workspace.Systems.QuqueRooms.*.JoinHitbox; cadangan: cari teks "Start Here" (BillboardGui/SurfaceGui) di Workspace lalu ambil posisi part
    /// induknya (Adornee bila diisi). Statis per server lobby → cache; kosong → scan ulang tiap 3 dtk.
    /// </summary>
    private long _roomsWs, _roomsAt;
    private List<(string Id, long Gui, Settings.Vec3 Pos)> _rooms = [];
    private static readonly System.Text.RegularExpressions.Regex PlayersRx = new(@"Players\s*(\d+)\s*/\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Room antrean: posisi JoinHitbox di-cache per Workspace; status RoomGui dibaca ulang tiap tick.
    /// "Start Here" = kosong; "Join Here" / "Players x/y" = sudah ada pemain lain.
    /// </summary>
    private IReadOnlyList<StartRoom> ReadRooms(long ws)
    {
        long now = Environment.TickCount64;
        if (ws != _roomsWs || (_rooms.Count == 0 && now - _roomsAt > 3000))
        {
            _roomsWs = ws;
            _roomsAt = now;
            _rooms = [];
            long systems = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.Systems) : 0;
            long rooms = systems != 0 ? reader.FindChild(systems, AnimeZeroPaths.QueueRooms) : 0;
            foreach (long room in rooms != 0 ? reader.GetChildren(rooms, 64) : [])
            {
                long hit = reader.FindChild(room, AnimeZeroPaths.JoinHitbox);
                if (hit != 0 && props.TryGetWorldPosition(hit, out float x, out float y, out float z))
                    _rooms.Add((reader.GetName(room), reader.FindChild(room, AnimeZeroPaths.RoomGui), new(x, y, z)));
            }
        }
        var list = new List<StartRoom>(_rooms.Count);
        foreach (var (id, gui, pos) in _rooms)
        {
            bool occupied = false;
            int? n = null, max = null;
            foreach (var (_, t) in VisibleTexts(gui, 200))
            {
                if (PlayersRx.Match(t) is { Success: true } m)
                {
                    n = int.Parse(m.Groups[1].Value);
                    max = int.Parse(m.Groups[2].Value);
                    if (n > 0) occupied = true;
                }
                else if (t.Contains(AnimeZeroPaths.RoomOccupiedText, StringComparison.OrdinalIgnoreCase)) occupied = true;
            }
            list.Add(new StartRoom(id, pos, occupied, n, max));
        }
        return list;
    }

    private IReadOnlyList<Settings.Vec3> ScanCircles(long ws)
    {
        long now = Environment.TickCount64;
        if (ws == _circlesWs && (_circles.Count > 0 || now - _circlesAt < 3000)) return _circles;
        _circlesWs = ws;
        _circlesAt = now;
        var found = new List<Settings.Vec3>();
        if (ws == 0) return _circles = found;

        if (_rooms.Count > 0) return _circles = found; // room utama sudah terbaca (ReadRooms)

        // Cadangan: teks "Start Here" di Workspace.
        var q = new Queue<(long Node, int Depth)>();
        q.Enqueue((ws, 0));
        int seen = 0;
        while (q.Count > 0 && seen++ < 25000)
        {
            var (node, depth) = q.Dequeue();
            string cls = reader.GetClassName(node);
            if (reader.GetName(node) == AnimeZeroPaths.Enemies) continue;
            if (PropertyReader.IsTextClass(cls) && Text(node) is { } t && t.Contains(AnimeZeroPaths.StartCircleText, StringComparison.OrdinalIgnoreCase))
            {
                if (PartAbove(node) is { } pos && !found.Any(f => Automation.Navigator.Distance2D(f.X, f.Z, pos.X, pos.Z) < 2)) found.Add(pos);
                continue;
            }
            if (depth >= 9) continue;
            foreach (long c in reader.GetChildren(node, 1024)) q.Enqueue((c, depth + 1));
        }
        return _circles = found;
    }

    private Settings.Vec3? PartAbove(long node)
    {
        long cur = reader.GetParent(node);
        for (int i = 0; i < 6 && cur != 0; i++)
        {
            if (reader.GetClassName(cur) is "BillboardGui" or "SurfaceGui"
                && reader.Offsets.TryResolve("Misc.Adornee", out long ao) && M.TryReadPointer(cur + ao, out long ad) && ad != 0
                && props.TryGetWorldPosition(ad, out float ax, out float ay, out float az))
                return new(ax, ay, az);
            if (props.TryGetWorldPosition(cur, out float x, out float y, out float z)) return new(x, y, z);
            cur = reader.GetParent(cur);
        }
        return null;
    }

    /// <summary>
    /// v0.2.92 [V] snapshot RIE 25 Okt 2026: Content.Row ×4 → judul kolom dari teks Hitbox.Title (bukan nama node), node =
    /// Hitbox.Clipping.ScrollingFrame.Slot ×5 diurutkan Y naik (atas → bawah = urutan beli). Node di luar kotak Clipping tidak diberi Rect.
    /// </summary>
    /// <summary>Nomor dari nama "SlotN" yang SelectedFrame-nya Visible (cek properti sendiri, bukan leluhur: daftar Roll tersembunyi).</summary>
    private int? SelectedSlot(long root, string list, string selPath)
    {
        long l = root != 0 ? reader.ResolvePath(root, list) : 0;
        if (l == 0) return null;
        int? hit = null;
        foreach (long c in reader.GetChildren(l, 64))
        {
            string name = reader.GetName(c);
            if (!name.StartsWith("Slot", StringComparison.Ordinal) || !int.TryParse(name[4..], out int n)) continue;
            long sel = reader.ResolvePath(c, selPath);
            if (sel == 0 || !reader.Offsets.TryResolve("GuiObject.Visible", out long vo) || !M.TryReadByte(sel + vo, out byte v)) continue;
            if (v != 0) { if (hit is not null) return null; hit = n; }
        }
        return hit;
    }

    private string? SkillUnitName(long tree, long pg)
    {
        int? st = SelectedSlot(tree, AnimeZeroPaths.SkillUnitList, AnimeZeroPaths.SkillUnitSelected);
        long roll = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.RollRoot) : 0;
        int? rs = SelectedSlot(roll, AnimeZeroPaths.RollUnitList, AnimeZeroPaths.RollUnitSelected);
        if (st is null || rs != st) return null;
        return CleanText(reader.ResolvePath(roll, AnimeZeroPaths.RollUnitTitle));
    }

    private List<SkillSlot> ReadSlots(long tree, long pg)
    {
        var list = new List<SkillSlot>();
        long content = reader.FindChild(tree, AnimeZeroPaths.SkillTreeContent);
        if (content == 0) return list;
        foreach (long row in reader.GetChildren(content, 16))
        {
            if (!reader.GetName(row).StartsWith("Row", StringComparison.Ordinal) || !IsShown(row, pg)) continue;
            string? col = CleanText(reader.ResolvePath(row, AnimeZeroPaths.SkillRowTitle))?.ToUpperInvariant();
            if (col is null || !Settings.UpgradeSettings.Columns.Contains(col)) continue;
            long sf = reader.ResolvePath(row, AnimeZeroPaths.SkillRowSlots);
            if (sf == 0) continue;
            var clip = HubRect(reader.ResolvePath(row, AnimeZeroPaths.SkillRowClipping));
            var slots = new List<(long Btn, GuiRect? R)>();
            foreach (long fr in reader.GetChildren(sf, 32))
            {
                if (reader.GetName(fr) != "Slot") continue;
                long btn = reader.FindChild(fr, "Slot");
                if (btn == 0) continue;
                slots.Add((btn, HubRect(btn)));
            }
            if (slots.Any(x => x.R is null)) continue; // urutan tidak pasti tanpa posisi
            slots.Sort((a, b) => a.R!.Value.Y.CompareTo(b.R!.Value.Y));
            for (int i = 0; i < slots.Count; i++)
            {
                long b = slots[i].Btn;
                long cb = reader.FindChild(b, AnimeZeroPaths.SlotCanBuy);
                long ps = reader.FindChild(b, AnimeZeroPaths.SlotPrice);
                int? price = ps != 0 && IsShown(ps, pg) ? ParseInt(CleanText(reader.FindChild(ps, "Price")) ?? CleanText(ps)) : null;
                long lk = reader.FindChild(b, AnimeZeroPaths.SlotLocked);
                long sel = reader.FindChild(b, AnimeZeroPaths.SlotSelected);
                var r = slots[i].R;
                bool inView = r is { } rr && clip is { } c && rr.CenterY >= c.Y && rr.CenterY <= c.Y + c.H && IsShown(b, pg);
                list.Add(new SkillSlot(col, i, cb != 0 && IsShown(cb, pg), inView ? r : null, price,
                    sel != 0 && IsShown(sel, pg), lk != 0 && IsShown(lk, pg)));
            }
        }
        return list;
    }

    private bool? ReadUltReady(long pg)
    {
        long ult = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.HudUltimate) : 0;
        if (ult == 0 || !IsShown(ult, pg)) return null;
        // v0.2.68: Locked tampil → belum bisa. Aktif (Progress tampil / tombol bukan "G", mis. "KAISEL") → G lagi = naga.
        // Penuh → ≥ 3 anak "ImageLabel" (kilau). Selain itu kosong/mengisi.
        long lk = reader.FindChild(ult, AnimeZeroPaths.HudUltimateLock);
        if (lk != 0 && IsShown(lk, pg)) return false;
        long prog = reader.FindChild(ult, AnimeZeroPaths.HudUltimateProgress);
        if (prog != 0 && IsShown(prog, pg)) return true;
        long frame = reader.ResolvePath(pg, AnimeZeroPaths.HudUltimateFrame);
        long keyNode = frame != 0 ? reader.ResolvePath(frame, AnimeZeroPaths.HudUltimateKey) : 0;
        string? key = keyNode != 0 ? Text(keyNode)?.Trim() : null;
        if (!string.IsNullOrEmpty(key) && !key.Equals("G", StringComparison.OrdinalIgnoreCase)) return true;
        int sparkles = reader.GetChildren(ult, 128).Count(c => reader.GetName(c) == AnimeZeroPaths.HudUltimateSparkle);
        return sparkles >= 3;
    }

    /// <summary>v0.2.79: true = Dash.Progress tampil (cooldown), false = tombol Dash terbaca tanpa Progress, null = tombol tidak terbaca.</summary>
    private bool? ReadDashCooling(long pg)
    {
        long dash = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.HudDash) : 0;
        if (dash == 0 || !IsShown(dash, pg)) return null;
        long prog = reader.FindChild(dash, "Progress");
        return prog != 0 && IsShown(prog, pg);
    }

    /// <summary>Cooldown skill dari HUD: tiap skillBox → (tombol, sisa detik atau null bila siap, terkunci).</summary>
    private List<SkillCooldown> ReadSkillCooldowns(long pg)
    {
        var list = new List<SkillCooldown>();
        long skills = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.HudSkills) : 0;
        if (skills == 0 || !IsShown(skills, pg)) return list;
        foreach (long box in reader.GetChildren(skills, 16))
        {
            if (reader.GetName(box) != AnimeZeroPaths.HudSkillBox) continue;
            long keyNode = reader.ResolvePath(box, AnimeZeroPaths.HudSkillKey);
            string? key = keyNode != 0 ? Text(keyNode)?.Trim() : null;
            if (string.IsNullOrEmpty(key)) continue;
            long cdNode = reader.ResolvePath(box, AnimeZeroPaths.HudSkillCd);
            string? cd = cdNode != 0 ? Text(cdNode)?.Trim() : null;
            long lockNode = reader.ResolvePath(box, AnimeZeroPaths.HudSkillLock);
            list.Add(new SkillCooldown(key, ParseCooldown(cd), lockNode != 0 && IsShown(lockNode, pg)));
        }
        return list;
    }

    /// <summary>"" → null (siap); "1.7"/"2" → detik; v0.2.79: teks lain = NaN (status tidak diketahui → skill tidak ditekan).</summary>
    public static double? ParseCooldown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().TrimEnd('s', 'S').Replace(',', '.');
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? (v <= 0 ? null : v) : double.NaN;
    }

    /// <summary>Musuh: Workspace.enemies.&lt;angka&gt; (Model) → HumanoidRootPart posisi, Humanoid Health/MaxHealth.</summary>
    /// <summary>
    /// Posisi rubble yang harus dihancurkan. Aktif bila teks HUD menyebut "rubble" atau RubbleBossBar Enabled.
    /// Posisi = Adornee BillboardGui PlayerGui.RubbleBar (offset Misc.Adornee), cadangan: parent BillboardGui bila di Workspace.
    /// </summary>
    private string? _rubbleInfo;
    private double? _rubbleHp;

    /// <summary>
    /// Snapshot RIE saat rubble aktif (Byzan, 24 Okt 2026, Hxh Area 3→4): BillboardGui PlayerGui.RubbleBar menempel ke
    /// Attachment "RubbleBar" di Workspace.World.Map.Border2.Blocker (Part dinding 101x57x4 di depan rubble).
    /// Target = titik dinding Blocker terdekat ke pemain (setinggi pemain), bukan pusat dinding.
    /// </summary>
    private Settings.Vec3? ReadRubble(long pg, long ws, string? hudRaw, Settings.Vec3? me, bool voteShown = false, bool enterHint = false)
    {
        _rubbleInfo = null; _rubbleHp = null; _rubbleStand = null;
        if (pg == 0) return null;
        bool hint = hudRaw?.Contains("rubble", StringComparison.OrdinalIgnoreCase) == true;
        long bossBar = reader.FindChild(pg, AnimeZeroPaths.RubbleBossBar);
        long bar = reader.FindChild(pg, AnimeZeroPaths.RubbleBar);
        // v0.2.63: rubble hancur sendiri bila lama tidak dipukul. Tanpa teks HUD "Break the rubble", rubble tetap dianggap aktif
        // bila BillboardGui RubbleBar ada dan Blocker yang ditempelinya masih padat (CanCollide true), agar langsung diserang.
        bool sure = hint || (bossBar != 0 && IsShown(bossBar, pg));
        // v0.2.73 (aturan Byzan 24 Okt 22:54): rubble HANYA diserang saat teks HUD atas "Break the rubble to continue!"
        // (atau RubbleBossBar tampil). Cadangan v0.2.63 (Blocker padat + RubbleBar) salah aktif sebelum wave mulai.
        if (!sure) return null;
        string info = "RubbleBar tidak ada";
        long holder = 0;
        if (bar != 0)
        {
            // Nyawa rubble: teks "87%" di BillboardGui RubbleBar.
            foreach (var t in Texts(bar, 0))
            {
                var m = System.Text.RegularExpressions.Regex.Match(t, @"(\d+(?:[.,]\d+)?)\s*%");
                if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                { _rubbleHp = pct / 100.0; break; }
            }
            if (!reader.Offsets.TryResolve("Misc.Adornee", out long ao)) info = "offset Misc.Adornee tidak ada";
            else if (!M.TryReadPointer(bar + ao, out long ad) || ad == 0) info = "Adornee kosong";
            else
            {
                string cls = reader.GetClassName(ad);
                info = $"Adornee {cls} {reader.GetName(ad)}";
                // Attachment tidak punya Position sendiri → pakai Part induknya (Blocker).
                holder = cls == "Attachment" ? reader.GetParent(ad) : ad;
            }
        }
        // v0.2.80 (jangan menebak): posisi rubble HANYA dari Adornee RubbleBar. Cadangan "Border*.Blocker pertama yang punya
        // RubbleBar" dihapus: semua Blocker punya RubbleBar sejak awal stage, jadi pilihannya tebakan.
        if (holder != 0 && props.TryGetWorldPosition(holder, out float x, out float y, out float z))
        {
            var c = new Settings.Vec3(x, y, z);
            _rubbleInfo = info;
            if (me is { } p && ParseVec(Prop(holder, "Size")) is { } sz && sz.X > 0)
            {
                // v0.2.91: Blocker Border3/Border4 Hxh diputar 90° (LookVector 1,0,0) → titik terdekat wajib ikut rotasi.
                var lv = ParseVec(Prop(holder, "LookVector"));
                _rubbleStand = RubbleStandPoint(holder, p);
                if (_rubbleStand is { } st)
                    _rubbleInfo += string.Create(System.Globalization.CultureInfo.InvariantCulture, $"; berdiri di ClimbStop ({st.X:0},{st.Z:0})");
                return RubbleTarget(c, sz, p, lv?.X ?? 0, lv?.Z ?? -1);
            }
            return c;
        }
        if (holder != 0 && PositionOf(holder) is { } hp) { _rubbleInfo = info; return hp; }
        _rubbleInfo = $"{info}; posisi tidak terbaca";
        return null;
    }

    private Settings.Vec3? _rubbleStand;

    /// <summary>
    /// Titik terdekat di kotak dinding ke pemain (ikut rotasi LookVector), Y = Y pemain.
    /// Size.X searah RightVector (-lz, lx), Size.Z searah LookVector (lx, lz). Default LookVector (0,0,-1) = rata sumbu.
    /// </summary>
    public static Settings.Vec3 RubbleTarget(Settings.Vec3 center, Settings.Vec3 size, Settings.Vec3 me, double lookX = 0, double lookZ = -1)
    {
        double l = Math.Sqrt(lookX * lookX + lookZ * lookZ);
        if (l < 1e-6) { lookX = 0; lookZ = -1; l = 1; }
        lookX /= l; lookZ /= l;
        double rx = -lookZ, rz = lookX;
        double dx = me.X - center.X, dz = me.Z - center.Z;
        double u = Math.Clamp(dx * rx + dz * rz, -Math.Abs(size.X) / 2, Math.Abs(size.X) / 2);
        double v = Math.Clamp(dx * lookX + dz * lookZ, -Math.Abs(size.Z) / 2, Math.Abs(size.Z) / 2);
        return new(center.X + u * rx + v * lookX, me.Y, center.Z + u * rz + v * lookZ);
    }

    /// <summary>
    /// v0.2.91 (snapshot RIE Byzan 25 Okt 00:59, Hxh Border4): di depan Blocker ada Border*.Rubble.ClimbStop (Part padat
    /// 200 stud tinggi, ~20 stud di depan dinding Border4) → karakter tidak pernah bisa ≤ 12 stud dari Blocker. Titik berdiri =
    /// titik ClimbStop terdekat (Border yang sama dengan Blocker Adornee RubbleBar). null bila tidak ada ClimbStop terbaca.
    /// </summary>
    private Settings.Vec3? RubbleStandPoint(long blocker, Settings.Vec3 me)
    {
        long border = reader.GetParent(blocker);
        long rub = border != 0 ? reader.FindChild(border, AnimeZeroPaths.RubbleName) : 0;
        if (rub == 0) return null;
        Settings.Vec3? best = null; double bd = double.MaxValue;
        foreach (long c in reader.GetChildren(rub, 256))
        {
            if (reader.GetName(c) != AnimeZeroPaths.RubbleClimbStop) continue;
            if (!props.TryGetWorldPosition(c, out float x, out float y, out float z)) continue;
            if (ParseVec(Prop(c, "Size")) is not { } sz) continue;
            var lv = ParseVec(Prop(c, "LookVector"));
            var t = RubbleTarget(new(x, y, z), sz, me, lv?.X ?? 0, lv?.Z ?? -1);
            double d = (t.X - me.X) * (t.X - me.X) + (t.Z - me.Z) * (t.Z - me.Z);
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    private List<EnemyInfo> ReadEnemies(long folder)
    {
        var list = new List<EnemyInfo>();
        if (folder == 0) return list;
        foreach (long m in reader.GetChildren(folder, 128))
        {
            long hrp = reader.FindChild(m, "HumanoidRootPart");
            if (hrp == 0 || !props.TryGetWorldPosition(hrp, out float x, out float y, out float z)) continue;
            double hp = 1, max = 1;
            long hum = reader.FindChild(m, "Humanoid");
            if (hum != 0)
            {
                var hp_ = props.Read(hum, "Humanoid");
                if (hp_.TryGetValue("Health", out var h)) hp = Num(h, 1);
                if (hp_.TryGetValue("MaxHealth", out var mh)) max = Num(mh, 1);
            }
            int hl = 0;
            foreach (long ch in reader.GetChildren(m, 64))
                if (reader.GetName(ch) == "Highlight" && reader.GetClassName(ch) == "Highlight") hl++;
            list.Add(new EnemyInfo(reader.GetName(m), new(x, y, z), hp, max, hl));
        }
        return list;
    }

    /// <summary>Item aktif di Workspace.PooledDrops (cash/health). Yang terparkir (Y = -5000) dilewati.</summary>
    private List<PickupInfo> ReadPickups(long ws)
    {
        var list = new List<PickupInfo>();
        long folder = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.PooledDrops) : 0;
        if (folder == 0) return list;
        foreach (long d in reader.GetChildren(folder, 256))
        {
            if (!props.TryGetWorldPosition(d, out float x, out float y, out float z) || y < AnimeZeroPaths.PooledDropParkedY) continue;
            list.Add(new PickupInfo(reader.GetName(d), new(x, y, z)));
        }
        return list;
    }

    /// <summary>v0.2.67: Workspace.AriseShadows.&lt;n&gt;.Arise (ProximityPrompt Enabled) → posisi Part induk + HoldDuration.</summary>
    public static (int Count, int Max)? ParseShadows(string? t)
    {
        var m = t is null ? null : System.Text.RegularExpressions.Regex.Match(t, @"(\d+)\s*/\s*(\d+)");
        return m is { Success: true } && int.TryParse(m.Groups[1].Value, out int c) && int.TryParse(m.Groups[2].Value, out int x) && x > 0 ? (c, x) : null;
    }

    private List<AriseInfo> ReadArises(long ws)
    {
        var list = new List<AriseInfo>();
        long folder = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.AriseShadows) : 0;
        if (folder == 0) return list;
        foreach (long part in reader.GetChildren(folder, 64))
        {
            long pr = reader.FindChild(part, AnimeZeroPaths.ArisePrompt);
            if (pr == 0 || reader.GetClassName(pr) != "ProximityPrompt") continue;
            if (!props.TryGetWorldPosition(part, out float x, out float y, out float z)) continue;
            var p = props.Read(pr, "ProximityPrompt");
            if (p.TryGetValue("Enabled", out var en) && en.Equals("false", StringComparison.OrdinalIgnoreCase)) continue;
            double hold = p.TryGetValue("HoldDuration", out var h) ? Num(h, 0.9) : 0.9;
            double max = p.TryGetValue("MaxActivationDistance", out var md) ? Num(md, 10) : 10;
            string name = p.TryGetValue("ObjectText", out var ot) && !string.IsNullOrWhiteSpace(ot) ? ot : reader.GetName(part);
            list.Add(new AriseInfo(name, new(x, y, z), hold is > 0 and < 10 ? hold : 0.9, max is > 1 and < 100 ? max : 10));
        }
        return list;
    }

    /// <summary>Darah karakter (Character.Humanoid Health/MaxHealth) 0..1.</summary>
    private double? ReadPlayerHp(long localPlayer)
    {
        if (localPlayer == 0 || !reader.Offsets.TryResolve("Player.ModelInstance", out long mo)) return null;
        if (!M.TryReadPointer(localPlayer + mo, out long character) || character == 0) return null;
        long hum = reader.FindChild(character, "Humanoid");
        if (hum == 0) return null;
        var p = props.Read(hum, "Humanoid");
        if (!p.TryGetValue("Health", out var h) || !p.TryGetValue("MaxHealth", out var mh)) return null;
        double max = Num(mh, 0);
        return max > 0 ? Math.Clamp(Num(h, 0) / max, 0, 1) : null;
    }

    private CameraInfo? ReadCamera(long ws)
    {
        if (ws == 0 || !reader.Offsets.TryResolve("Workspace.CurrentCamera", out long co)) return null;
        if (!M.TryReadPointer(ws + co, out long cam) || cam == 0) return null;
        var p = props.Read(cam, "Camera");
        if (!p.TryGetValue("Position", out var pos) || !p.TryGetValue("LookVector", out var lv) || !p.TryGetValue("ViewportSize", out var vs)) return null;
        var a = Vec(pos); var l = Vec(lv); var v = Nums(vs);
        if (a is null || l is null || v.Length < 2) return null;
        double fov = p.TryGetValue("FieldOfView", out var f) ? Num(f, 70) : 70;
        string? subj = p.TryGetValue("CameraSubject", out var cs) && cs != "nil" && cs.Length > 0 ? cs : null;
        return new CameraInfo(a, l, fov, v[0], v[1]) { Subject = subj };
    }

    private static double[] Nums(string s) => s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(t => double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : double.NaN)
        .Where(d => !double.IsNaN(d)).ToArray();
    private static Settings.Vec3? Vec(string s) { var n = Nums(s); return n.Length == 3 ? new(n[0], n[1], n[2]) : null; }
    private static double Num(string s, double fallback) { var n = Nums(s); return n.Length > 0 ? n[0] : fallback; }

    /// <summary>Posisi HumanoidRootPart karakter: Player.ModelInstance → Character → HumanoidRootPart.</summary>
    private Settings.Vec3? ReadPlayerPos(long localPlayer)
    {
        if (localPlayer == 0 || !reader.Offsets.TryResolve("Player.ModelInstance", out long mo)) return null;
        if (!M.TryReadPointer(localPlayer + mo, out long character) || character == 0) return null;
        long hrp = reader.FindChild(character, "HumanoidRootPart");
        return hrp != 0 && props.TryGetWorldPosition(hrp, out float x, out float y, out float z) ? new(x, y, z) : null;
    }

    /// <summary>LookVector kamera (XZ) dari Workspace.CurrentCamera.</summary>
    private (double X, double Z)? ReadCameraLook(long ws)
    {
        if (ws == 0 || !reader.Offsets.TryResolve("Workspace.CurrentCamera", out long co)) return null;
        if (!M.TryReadPointer(ws + co, out long cam) || cam == 0) return null;
        if (!props.Read(cam, "Camera").TryGetValue("LookVector", out var lv)) return null;
        var p = lv.Split(',', StringSplitOptions.TrimEntries);
        return p.Length == 3 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x)
               && double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double z)
            ? (x, z) : null;
    }

    /// <summary>Posisi area ke-n: Workspace.World.Spawns[n] lalu Areas[n] (Part, Model, atau Folder berisi Part).</summary>
    private Settings.Vec3? WsPos(long ws, string path)
    {
        long part = ws != 0 ? reader.ResolvePath(ws, path) : 0;
        return part != 0 && props.TryGetWorldPosition(part, out float x, out float y, out float z) ? new(x, y, z) : null;
    }

    private Settings.Vec3? ReadAreaCenter(long ws, int n)
    {
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        long areas = world != 0 ? reader.FindChild(world, AnimeZeroPaths.WorldAreas) : 0;
        long part = areas != 0 ? reader.FindChild(areas, n.ToString()) : 0;
        return part != 0 && props.TryGetWorldPosition(part, out float x, out float y, out float z) ? new(x, y, z) : null;
    }

    private Settings.Vec3? ReadAreaPos(long ws, int n)
    {
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        if (world == 0) return null;
        Settings.Vec3? Pos(string folder)
        {
            long f = reader.FindChild(world, folder);
            long node = f != 0 ? reader.FindChild(f, n.ToString()) : 0;
            return node != 0 ? PositionOf(node) : null;
        }
        // v0.2.47: AreaEntries.<n> dulu (titik masuk resmi, sama dengan penanda "AREA n" di layar). Hanya ada untuk sebagian area.
        if (Pos(AnimeZeroPaths.WorldAreaEntries) is { } entry) return entry;
        // v0.2.50: Spawns.<n> ada di ujung area n-1 (Hxh: Spawns.4 (158,-198) masih di kotak Area 3, penanda AREA 4 ~150 stud lagi).
        // → titik kotak Areas.<n> terdekat ke Spawns.<n>, masuk 8 stud ke dalam = tepi masuk area n.
        var spawn = Pos(AnimeZeroPaths.WorldSpawns);
        long areas = reader.FindChild(world, AnimeZeroPaths.WorldAreas);
        long part = areas != 0 ? reader.FindChild(areas, n.ToString()) : 0;
        if (part != 0 && props.TryGetWorldPosition(part, out float ax, out float ay, out float az))
        {
            var size = ParseVec(Prop(part, "Size"));
            var center = new Settings.Vec3(ax, ay, az);
            if (spawn is { } sp && size is { } bs && bs.X > 0) return AreaEntryPoint(center, bs, sp);
            return spawn ?? center;
        }
        return spawn;
    }

    /// <summary>Titik kotak area (center/size) terdekat ke <paramref name="from"/>, digeser <paramref name="inset"/> stud ke dalam. Y = Y titik asal.</summary>
    public static Settings.Vec3 AreaEntryPoint(Settings.Vec3 center, Settings.Vec3 size, Settings.Vec3 from, double inset = 8)
    {
        double hx = Math.Max(0, size.X / 2 - inset), hz = Math.Max(0, size.Z / 2 - inset);
        double x = Math.Clamp(from.X, center.X - hx, center.X + hx), z = Math.Clamp(from.Z, center.Z - hz, center.Z + hz);
        return new(x, from.Y, z);
    }

    private Settings.Vec3? PositionOf(long node, int depth = 0)
    {
        if (props.TryGetWorldPosition(node, out float x, out float y, out float z)) return new(x, y, z);
        if (depth >= 2) return null;
        foreach (long c in reader.GetChildren(node, 64))
            if (PositionOf(c, depth + 1) is { } p) return p;
        return null;
    }

    /// <summary>
    /// Entri daftar Create Lobby (map/act/mode) [V]: anak GuiObject yang punya teks. Terkunci = ada turunan terlihat
    /// bernama "*Lock*" atau teks memuat "lock". Terpilih = turunan terlihat bernama "*Select*"/"*Selected*".
    /// </summary>
    private List<LobbyEntry> ReadList(long list, long pg)
    {
        var result = new List<LobbyEntry>();
        list = EntryContainer(list);
        if (list == 0) return result;
        foreach (long c in reader.GetChildren(list, 64))
        {
            string cls = reader.GetClassName(c);
            if (!PropertyReader.IsGuiObject(cls) || !IsShown(c, pg) || IsListChrome(reader.GetName(c))) continue;
            // Kartu map terverifikasi RIE (23 Okt 2026): MapsList.List.ScrollingFrame.<Id>.<Id>.Txt = judul,
            // .Holder.Lvl.Label = "Mode: ", .Holder.Lvl.Value = "Story"/"Raid"/"Tidal High Rise"/...
            long inner = reader.FindChild(c, reader.GetName(c));
            long title = inner != 0 ? reader.FindChild(inner, "Txt") : 0;
            if (inner != 0 && Text(title) is { Length: > 0 } cardTitle)
            {
                string cardName = System.Text.RegularExpressions.Regex.Replace(cardTitle, "<[^>]+>", "").Trim();
                string? cardMode = Text(reader.ResolvePath(inner, "Holder.Lvl.Value"))?.Trim();
                bool cardLocked = HasShownDescendant(c, pg, "lock", 0);
                result.Add(new LobbyEntry(cardName, cardLocked, HasShownDescendant(c, pg, "select", 0), MenuRect(inner) ?? MenuRect(c), string.IsNullOrEmpty(cardMode) ? null : cardMode));
                continue;
            }
            if (reader.GetName(c).Contains("Spacer", StringComparison.OrdinalIgnoreCase)) continue;
            // Cadangan (daftar mode/act): judul = teks pertama.
            var texts = Texts(c, 0).ToList();
            var clean = texts.Select(t => System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "").Trim()).ToList();
            int mi = clean.FindIndex(t => t.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase));
            string? mode = mi < 0 ? null : clean[mi][5..].Trim() is { Length: > 0 } inline ? inline
                : mi + 1 < clean.Count ? clean[mi + 1] : null; // label "Mode:" dan nilainya bisa TextLabel terpisah
            string name = texts.Select(t => System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "").Trim())
                .FirstOrDefault(t => t.Length > 0 && !t.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase) && !t.Equals("NEW!", StringComparison.OrdinalIgnoreCase))
                ?? reader.GetName(c);
            if (string.IsNullOrWhiteSpace(name)) continue;
            bool locked = name.Contains("lock", StringComparison.OrdinalIgnoreCase) || HasShownDescendant(c, pg, "lock", 0);
            bool selected = HasShownDescendant(c, pg, "select", 0);
            result.Add(new LobbyEntry(name.Trim(), locked, selected, MenuRect(c), mode));
        }
        return result;
    }

    /// <summary>Rect tampak daftar (ScrollingFrame / wadah entri). Entri di luar rect ini tersembunyi dan perlu di-scroll.</summary>
    private GuiRect? ListView(long list)
    {
        if (list == 0) return null;
        long box = EntryContainer(list);
        return MenuRect(box) ?? MenuRect(list);
    }

    /// <summary>Tombol difficulty terverifikasi: Container.&lt;D&gt;.&lt;D&gt;.Hit (TextButton seukuran kartu).</summary>
    private List<LobbyEntry> ReadDifficulties(long lobby, long pg)
    {
        var found = new List<LobbyEntry>();
        long box = reader.ResolvePath(lobby, AnimeZeroPaths.LobbyDifficultyContainer);
        if (box == 0) return found;
        string? sel = CleanText(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyDifficultyTagPath))?.Trim('[', ']', ' ');
        foreach (string d in Enum.GetNames<Settings.Difficulty>())
        {
            long outer = reader.FindChild(box, d);
            long card = outer != 0 ? reader.FindChild(outer, d) : 0;
            long hit = card != 0 ? reader.FindChild(card, "Hit") : 0;
            var r = MenuRect(hit) ?? MenuRect(card) ?? MenuRect(outer);
            if (r is null || !IsShown(outer, pg)) continue;
            found.Add(new LobbyEntry(d, false, sel is not null && sel.Equals(d, StringComparison.OrdinalIgnoreCase), r));
        }
        return found;
    }

    /// <summary>
    /// Kartu act terverifikasi: ActsList.List.ScrollingFrame.Act&lt;n&gt; (Frame; ada banyak "Act1" untuk tiap map, yang
    /// Visible milik map terpilih) → .Act&lt;n&gt; ImageButton → .TxtHolder.Act ("Act 1") / .TxtHolder.Title, .Locked.
    /// </summary>
    private List<LobbyEntry> ReadActs(long lobby, long pg)
    {
        long scroll = reader.ResolvePath(lobby, AnimeZeroPaths.LobbyActsScroll);
        if (scroll == 0) return ReadList(Find(lobby, AnimeZeroPaths.LobbyActsList), pg);
        var result = new List<LobbyEntry>();
        foreach (long c in reader.GetChildren(scroll, 128))
        {
            string n = reader.GetName(c);
            if (!n.StartsWith("Act", StringComparison.Ordinal) || !IsShown(c, pg)) continue;
            long btn = reader.FindChild(c, n);
            if (btn == 0) continue;
            string act = CleanText(reader.ResolvePath(btn, "TxtHolder.Act")) ?? n.Replace("Act", "Act ");
            string? title = CleanText(reader.ResolvePath(btn, "TxtHolder.Title"));
            long lockF = reader.FindChild(btn, "Locked");
            bool locked = lockF != 0 && IsShown(lockF, pg);
            if (MenuRect(btn) is not { } r) continue;
            result.Add(new LobbyEntry(title is null ? act : $"{act} {title}", locked, false, r));
        }
        return result;
    }

    /// <summary>
    /// MapsList dkk. bisa berupa wadah {ScrollingBar, List}; entri map ada di dalam "List" (bug v0.2: dropdown berisi
    /// "ScrollingBar" dan "List"). Turun ke anak List / ScrollingFrame / Container / Content selama wadah belum berisi entri.
    /// </summary>
    private long EntryContainer(long list)
    {
        for (int i = 0; i < 3 && list != 0; i++)
        {
            long next = 0;
            int entries = 0;
            foreach (long c in reader.GetChildren(list, 64))
            {
                string n = reader.GetName(c), cls = reader.GetClassName(c);
                if (!PropertyReader.IsGuiObject(cls)) continue;
                if (cls == "ScrollingFrame" || n is "List" or "Container" or "Content" or "Holder" or "Items") next = next == 0 ? c : next;
                else if (!IsListChrome(n)) entries++;
            }
            if (next == 0 || entries > 1) return list;
            list = next;
        }
        return list;
    }

    private static bool IsListChrome(string name) =>
        name.Contains("Scroll", StringComparison.OrdinalIgnoreCase) || name.StartsWith("UI", StringComparison.Ordinal) ||
        name.StartsWith('_') || name.Contains("Padding", StringComparison.OrdinalIgnoreCase) || name.Contains("Divider", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Template", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> Texts(long node, int depth)
    {
        if (PropertyReader.IsTextClass(reader.GetClassName(node)) && Text(node) is { Length: > 0 } t) yield return t;
        if (depth >= 3) yield break;
        foreach (long c in reader.GetChildren(node, 32))
            foreach (var s in Texts(c, depth + 1)) yield return s;
    }

    private string? FirstText(long node, int depth)
    {
        if (PropertyReader.IsTextClass(reader.GetClassName(node)) && Text(node) is { Length: > 0 } t) return t;
        if (depth >= 3) return null;
        foreach (long c in reader.GetChildren(node, 32))
            if (FirstText(c, depth + 1) is { } s) return s;
        return null;
    }

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

    /// <summary>Cari turunan bernama <paramref name="name"/> (BFS, maks 9 level / 6000 node), dengan cache.</summary>
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

    /// <summary>Terlihat = Visible pada dirinya dan semua leluhur GuiObject, serta ScreenGui-nya Enabled.</summary>
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

    /// <summary>Nilai anak "Tag" (StringValue) atau Text, untuk MapLine/ActLine [V].</summary>
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

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
        long voteYesNode = vote != 0 ? Find(vote, AnimeZeroPaths.StartVoteYes) : 0;
        bool voteShown = vote != 0 && IsShown(vote, pg) && voteYesNode != 0 && IsShown(voteYesNode, pg);
        GuiRect? voteYes = voteShown ? Rect(voteYesNode) : null;

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
        var playerPos = ReadPlayerPos(roots.LocalPlayer);

        var snap = new GameSnapshot
        {
            Attached = true,
            PlayerPos = playerPos,
            CameraLook = ReadCameraLook(ws),
            Camera = ReadCamera(ws),
            Enemies = ReadEnemies(enemies),
            NextAreaPos = hud is { AreaTotal: > 0 } && hud.Area < hud.AreaTotal ? ReadAreaPos(ws, hud.Area + 1) : null,
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
            DashCooling = pg != 0 && reader.ResolvePath(pg, AnimeZeroPaths.HudDashProgress) is long dp && dp != 0 && IsShown(dp, pg),
            QueueSkipCosts = qCost != 0 && IsShown(qCost, pg),
            LobbyMapTag = lobbyShown ? CleanText(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyMapTitlePath)) ?? Tag(Find(lobby, AnimeZeroPaths.LobbyMapLine)) : null,
            LobbyActTag = lobbyShown ? CleanText(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyActTagPath)) ?? Tag(Find(lobby, AnimeZeroPaths.LobbyActLine)) : null,
            LobbyDifficultyTag = lobbyShown ? CleanText(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyDifficultyTagPath))?.Trim('[', ']', ' ') : null,
            LobbyStart = lobbyShown ? MenuRect(reader.ResolvePath(lobby, AnimeZeroPaths.LobbyStartPath)) ?? MenuRect(Find(lobby, AnimeZeroPaths.LobbyStartButton)) : null,
            LobbyMapsView = lobbyShown ? ListView(Find(lobby, AnimeZeroPaths.LobbyMapsList)) : null,
            LobbyActsView = lobbyShown ? ListView(Find(lobby, AnimeZeroPaths.LobbyActsList)) : null,
            LobbyModesView = lobbyShown ? ListView(Find(lobby, AnimeZeroPaths.LobbyModesList)) : null,
            LobbyDifficulties = lobbyShown ? ReadDifficulties(lobby, pg) : [],
            SkillTreeOpen = treeShown,
            Tokens = treeShown ? ParseInt(Text(reader.ResolvePath(tree, AnimeZeroPaths.SkillTreeTokens))) : null,
            Slots = treeShown ? ReadSlots(tree, pg) : [],
            BuyButton = treeShown ? VisibleRect(Find(Find(tree, AnimeZeroPaths.BuyFrame), AnimeZeroPaths.BuyButton), pg) : null,
        };
        snap = ReadModes(snap, pg, ws, lobby, lobbyShown, end, endShown);
        if (snap.Screen != GameScreen.Lobby) return snap;
        var (areas, cards) = ScanHub(pg);
        var startRooms = ReadRooms(ws);
        return snap with { AreasButton = areas, TeleportCards = cards, StartRooms = startRooms, StartCircles = ScanCircles(ws) };
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
        return _hub = (areas, cards);
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
    /// Slot SkillTree: tiap kolom (ATK/CRIT/DEF/SPD) dicari per nama; anak GuiObject-nya diurutkan per posisi Y (bawah →
    /// atas = urutan beli) [V]. Slot bisa dibeli bila anak CanBuy terlihat.
    /// </summary>
    private List<SkillSlot> ReadSlots(long tree, long pg)
    {
        var list = new List<SkillSlot>();
        foreach (string col in Settings.UpgradeSettings.Columns)
        {
            long colNode = Find(tree, col);
            if (colNode == 0) continue;
            var slots = reader.GetChildren(colNode, 64)
                .Where(c => PropertyReader.IsGuiObject(reader.GetClassName(c)) && reader.FindChild(c, AnimeZeroPaths.SlotCanBuy) != 0)
                .Select(c => (Node: c, Rect: Rect(c)))
                .OrderByDescending(x => x.Rect?.Y ?? 0)
                .ToList();
            for (int i = 0; i < slots.Count; i++)
            {
                long canBuy = reader.FindChild(slots[i].Node, AnimeZeroPaths.SlotCanBuy);
                list.Add(new SkillSlot(col, i, canBuy != 0 && IsShown(canBuy, pg), slots[i].Rect));
            }
        }
        return list;
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

    /// <summary>"" → null (siap); "1.7"/"2" → detik; teks lain yang tak terbaca dianggap cooldown 1 dtk.</summary>
    public static double? ParseCooldown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().TrimEnd('s', 'S').Replace(',', '.');
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? (v <= 0 ? null : v) : 1;
    }

    /// <summary>Musuh: Workspace.enemies.&lt;angka&gt; (Model) → HumanoidRootPart posisi, Humanoid Health/MaxHealth.</summary>
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
            list.Add(new EnemyInfo(reader.GetName(m), new(x, y, z), hp, max));
        }
        return list;
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
        return new CameraInfo(a, l, fov, v[0], v[1]);
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
    private Settings.Vec3? ReadAreaPos(long ws, int n)
    {
        long world = ws != 0 ? reader.FindChild(ws, AnimeZeroPaths.World) : 0;
        if (world == 0) return null;
        foreach (string folder in new[] { AnimeZeroPaths.WorldSpawns, AnimeZeroPaths.WorldAreas })
        {
            long f = reader.FindChild(world, folder);
            long node = f != 0 ? reader.FindChild(f, n.ToString()) : 0;
            if (node != 0 && PositionOf(node) is { } pos) return pos;
        }
        return null;
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

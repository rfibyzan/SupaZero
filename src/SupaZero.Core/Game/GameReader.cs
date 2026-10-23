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
public sealed class GameReader(InstanceReader reader, PropertyReader props)
{
    private readonly Dictionary<string, long> _cache = new(StringComparer.Ordinal);

    public double GuiInsetY { get; set; } = ScreenMap.DefaultGuiInsetY;
    public double GuiScale { get; set; } = 1;

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
        bool voteShown = vote != 0 && IsShown(vote, pg);
        GuiRect? voteYes = voteShown ? Rect(Find(vote, AnimeZeroPaths.StartVoteYes)) : null;

        // Layar akhir
        long end = Find(pg, AnimeZeroPaths.EndScreen);
        bool endShown = end != 0 && IsShown(end, pg);
        long result = endShown ? Find(end, AnimeZeroPaths.ResultText) : Find(pg, AnimeZeroPaths.ResultText);
        string? resultText = result != 0 && IsShown(result, pg) ? Text(result) : null;
        long boss = Find(pg, AnimeZeroPaths.BossBar);

        // Lobby
        long lobby = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.LobbyRoot) : 0;
        bool lobbyShown = lobby != 0 && IsShown(lobby, pg);

        // SkillTree
        long tree = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.SkillTreeRoot) : 0;
        bool treeShown = tree != 0 && IsShown(tree, pg);

        var hud = HudText.Parse(hudRaw);
        var playerPos = ReadPlayerPos(roots.LocalPlayer);

        return new GameSnapshot
        {
            Attached = true,
            PlayerPos = playerPos,
            CameraLook = ReadCameraLook(ws),
            NextAreaPos = hud is { AreaTotal: > 0 } && hud.Area < hud.AreaTotal ? ReadAreaPos(ws, hud.Area + 1) : null,
            LobbyModes = lobbyShown ? ReadList(Find(lobby, AnimeZeroPaths.LobbyModesList), pg) : [],
            LobbyMaps = lobbyShown ? ReadList(Find(lobby, AnimeZeroPaths.LobbyMapsList), pg) : [],
            LobbyActs = lobbyShown ? ReadList(Find(lobby, AnimeZeroPaths.LobbyActsList), pg) : [],
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
            LobbyMapTag = lobbyShown ? Tag(Find(lobby, AnimeZeroPaths.LobbyMapLine)) : null,
            LobbyActTag = lobbyShown ? Tag(Find(lobby, AnimeZeroPaths.LobbyActLine)) : null,
            LobbyStart = lobbyShown ? Rect(Find(lobby, AnimeZeroPaths.LobbyStartButton)) : null,
            SkillTreeOpen = treeShown,
            Tokens = treeShown ? ParseInt(Text(reader.ResolvePath(tree, AnimeZeroPaths.SkillTreeTokens))) : null,
            Slots = treeShown ? ReadSlots(tree, pg) : [],
            BuyButton = treeShown ? VisibleRect(Find(Find(tree, AnimeZeroPaths.BuyFrame), AnimeZeroPaths.BuyButton), pg) : null,
        };
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
        if (list == 0) return result;
        foreach (long c in reader.GetChildren(list, 64))
        {
            string cls = reader.GetClassName(c);
            if (!PropertyReader.IsGuiObject(cls) || !IsShown(c, pg)) continue;
            string name = FirstText(c, 0) ?? reader.GetName(c);
            if (string.IsNullOrWhiteSpace(name)) continue;
            bool locked = name.Contains("lock", StringComparison.OrdinalIgnoreCase) || HasShownDescendant(c, pg, "lock", 0);
            bool selected = HasShownDescendant(c, pg, "select", 0);
            result.Add(new LobbyEntry(name.Trim(), locked, selected, Rect(c)));
        }
        return result;
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

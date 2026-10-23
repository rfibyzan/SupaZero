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

        return new GameSnapshot
        {
            Attached = true,
            PlaceId = placeId,
            LocalPlayer = roots.LocalPlayer != 0 ? reader.GetName(roots.LocalPlayer) : string.Empty,
            HudRaw = hudRaw,
            Hud = HudText.Parse(hudRaw),
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

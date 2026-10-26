using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    private (GuiRect?, IReadOnlyList<LobbyEntry>) _hub = (null, []);
    private long _circlesWs, _circlesAt;
    private IReadOnlyList<Settings.Vec3> _circles = [];

    /// <summary>Manual RIE path (optional) for the Areas button / Story card; overrides text detection. May be "game.Players.X.PlayerGui...." or relative to PlayerGui.</summary>
    public string? AreasButtonPath { get; set; }
    public string? StoryCardPath { get; set; }

    /// <summary>Path (relative to PlayerGui) of the node last picked as the Areas button / teleport card, for logging & matching in RIE.</summary>
    public string? LastAreasPick { get; private set; }
    public string? LastCardPick { get; private set; }

    /// <summary>"Areas" button + "Click to Teleport" cards in PlayerGui (scan of visible text, at most every 700 ms).</summary>
    private (GuiRect?, IReadOnlyList<LobbyEntry>) ScanHub(long pg)
    {
        long now = Environment.TickCount64;
        if (now - _hubAt < 250) return _hub;
        _hubAt = now;
        GuiRect? areas = null;
        var cards = new List<LobbyEntry>();

        // 1) manual path from Settings, 2) RIE-verified path, 3) text scan.
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
        bool areasPathExists = manualAreas != 0; // button exists (may be covered by the currently open Areas panel)
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
        // v0.2.85: other cards in the Areas panel (e.g. "Traits" for power calibration) → text scan only inside the panel's Container.
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
    /// Trait / Spin menu buttons in PlayerGui.LobbyHUD (node name or visible text contains "trait" / "spin" / "roll").
    /// Only LobbyHUD (left navigation buttons), never the contents of the Spin/Trait menu (the roll buttons there use Spins/Rerolls).
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
    /// Button for a text label. Order: the label itself if it is a button → button ancestor (max 4 levels) that
    /// covers the label and is not oversized → nearest button around the label (sibling / cousin, e.g. icon above the text)
    /// → the label's own rect. NEVER uses a container frame (previously could land in the middle of the sidebar = another button).
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

        // Nearby buttons: check descendants of parent & grandparent (max 2 levels down), take the closest.
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

    /// <summary>Teleport card title: other text on the same card (search the parent, then grandparent).</summary>
    private string? CardTitle(long hint)
    {
        long scope = reader.GetParent(hint);
        for (int up = 0; up < 3 && scope != 0; up++, scope = reader.GetParent(scope))
            if (SiblingTitle(scope, hint) is { } t) return t;
        return null;
    }

    /// <summary>PlayerGui BFS that skips disabled ScreenGuis / invisible GuiObjects; result = (text node, text content).</summary>
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

    /// <summary>Climb up to 4 levels to an ImageButton/TextButton; none → direct parent (card) or the node itself.</summary>
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

    /// <summary>Other text under the same parent (card title, e.g. "Story").</summary>
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
    /// Create Room circles: Workspace.Systems.QuqueRooms.*.JoinHitbox; fallback: find "Start Here" text (BillboardGui/SurfaceGui) in Workspace and take the position of its
    /// parent part (Adornee if set). Static per lobby server → cached; empty → rescan every 3 s.
    /// </summary>
    private long _roomsWs, _roomsAt;
    private List<(string Id, long Gui, Settings.Vec3 Pos)> _rooms = [];
    private static readonly System.Text.RegularExpressions.Regex PlayersRx = new(@"Players\s*(\d+)\s*/\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Queue rooms: JoinHitbox positions cached per Workspace; RoomGui status re-read every tick.
    /// "Start Here" = empty; "Join Here" / "Players x/y" = other players already present.
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

        if (_rooms.Count > 0) return _circles = found; // main rooms already read (ReadRooms)

        // Fallback: "Start Here" text in Workspace.
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
    /// v0.2.92 [V] RIE snapshot 25 Oct 2026: Content.Row ×4 → column title from Hitbox.Title text (not the node name), nodes =
    /// Hitbox.Clipping.ScrollingFrame.Slot ×5 sorted by ascending Y (top → bottom = buy order). Nodes outside the Clipping box get no Rect.
    /// </summary>
    /// <summary>Number from the "SlotN" name whose SelectedFrame is Visible (checks its own property, not ancestors: the Roll list is hidden).</summary>
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
}

using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    /// <summary>
    /// Create Lobby list entries (map/act/mode) [V]: GuiObject children that have text. Locked = has a visible descendant
    /// named "*Lock*" or text containing "lock". Selected = visible descendant named "*Select*"/"*Selected*".
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
            // Map card verified in RIE (23 Oct 2026): MapsList.List.ScrollingFrame.<Id>.<Id>.Txt = title,
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
            // Fallback (mode/act list): title = first text.
            var texts = Texts(c, 0).ToList();
            var clean = texts.Select(t => System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "").Trim()).ToList();
            int mi = clean.FindIndex(t => t.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase));
            string? mode = mi < 0 ? null : clean[mi][5..].Trim() is { Length: > 0 } inline ? inline
                : mi + 1 < clean.Count ? clean[mi + 1] : null; // the "Mode:" label and its value can be separate TextLabels
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

    /// <summary>Visible rect of the list (ScrollingFrame / entry container). Entries outside this rect are hidden and need scrolling.</summary>
    private GuiRect? ListView(long list)
    {
        if (list == 0) return null;
        long box = EntryContainer(list);
        return MenuRect(box) ?? MenuRect(list);
    }

    /// <summary>Verified difficulty buttons: Container.&lt;D&gt;.&lt;D&gt;.Hit (card-sized TextButton).</summary>
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
    /// Verified act cards: ActsList.List.ScrollingFrame.Act&lt;n&gt; (Frame; there are many "Act1" nodes, one per map, the
    /// Visible one belongs to the selected map) → .Act&lt;n&gt; ImageButton → .TxtHolder.Act ("Act 1") / .TxtHolder.Title, .Locked.
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
    /// MapsList etc. can be a container {ScrollingBar, List}; map entries live inside "List" (v0.2 bug: dropdown showed
    /// "ScrollingBar" and "List"). Descend into a List / ScrollingFrame / Container / Content child while the container has no entries yet.
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
}

using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    private List<LobbyEntry> ReadSkillUnits(long tree)
    {
        var res = new List<LobbyEntry>();
        long l = reader.ResolvePath(tree, AnimeZeroPaths.SkillUnitList);
        if (l == 0 || !reader.Offsets.TryResolve("GuiObject.Visible", out long vo)) return res;
        foreach (long c in reader.GetChildren(l, 64))
        {
            string name = reader.GetName(c);
            if (!name.StartsWith("Slot", StringComparison.Ordinal) || !int.TryParse(name[4..], out _)) continue;
            long lv = reader.ResolvePath(c, AnimeZeroPaths.SkillUnitLevel), sel = reader.ResolvePath(c, AnimeZeroPaths.SkillUnitSelected);
            bool owned = lv != 0 && M.TryReadByte(lv + vo, out byte lvv) && lvv != 0 && CleanText(lv) is not null;
            bool selected = sel != 0 && M.TryReadByte(sel + vo, out byte sv) && sv != 0;
            res.Add(new LobbyEntry(name, !owned, selected, HubRect(reader.ResolvePath(c, AnimeZeroPaths.SkillUnitButton))));
        }
        return res;
    }

    /// <summary>v0.2.99: diagnosa nama unit (slot SkillTree vs slot Roll + judul) untuk log bila nama tidak terbaca.</summary>
    public string? SkillUnitDebug { get; private set; }
    private readonly RollTitleTracker _rollTitle = new();

    private string? ResetRollTitle() { _rollTitle.Reset(); return null; }

    private string? SkillUnitName(long tree, long pg)
    {
        int? st = SelectedSlot(tree, AnimeZeroPaths.SkillUnitList, AnimeZeroPaths.SkillUnitSelected);
        long roll = pg != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.RollRoot) : 0;
        int? rs = SelectedSlot(roll, AnimeZeroPaths.RollUnitList, AnimeZeroPaths.RollUnitSelected);
        string? title = roll != 0 ? CleanText(reader.ResolvePath(roll, AnimeZeroPaths.RollUnitTitle)) : null;
        SkillUnitDebug = $"SkillTree slot {st?.ToString() ?? "?"}, Roll {(roll == 0 ? "tidak ada" : $"slot {rs?.ToString() ?? "?"}")}, judul Roll \"{title ?? "-"}\"";
        long rl = roll != 0 ? reader.ResolvePath(roll, AnimeZeroPaths.RollUnitList) : 0;
        bool rollHasSlots = rl != 0 && reader.GetChildren(rl, 64).Any(c => reader.GetName(c) is var nm && nm.StartsWith("Slot", StringComparison.Ordinal) && int.TryParse(nm[4..], out _));
        if (!rollHasSlots) SkillUnitDebug += " (daftar Roll kosong → judul diterima hanya bila berubah setelah ganti slot)";
        return _rollTitle.Resolve(st, title, rollHasSlots, rs);
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
}

namespace SupaZero.Core.Game;

/// <summary>
/// v0.3.0 [V] snapshot 08:19:35 (25 Oct): Roll.CharacterSlots.Fade.List can be EMPTY (only SlotTemp; the Roll menu has not been opened since joining),
/// while Roll.CharacterInfo.Title still follows the unit selected in SkillTree (SkillTree Slot5 selected, Title "Twisted Agony").
/// Without the Roll list, the title's slot cannot be matched directly, so a title is only accepted once it is PROVEN to have changed after the SkillTree slot switched
/// (title at the switch ≠ current title). A title that never changed = stale/unknown → null (no guessing).
/// </summary>
public sealed class RollTitleTracker
{
    private int? _st;
    private string? _titleAtSwitch;
    private readonly Dictionary<int, string> _proven = new();

    public void Reset() { _st = null; _titleAtSwitch = null; _proven.Clear(); }

    /// <param name="st">slot selected in SkillTree</param>
    /// <param name="rollHasSlots">the Roll list has SlotN children</param>
    /// <param name="rs">slot selected in the Roll list</param>
    public string? Resolve(int? st, string? title, bool rollHasSlots, int? rs)
    {
        if (rollHasSlots) { if (st is int a && rs == a && title is not null) _proven[a] = title; Track(st, title); return st is not null && rs == st ? title : null; }
        Track(st, title);
        if (st is not int s || title is null) return null;
        if (_titleAtSwitch is not null && !title.Equals(_titleAtSwitch, StringComparison.Ordinal)) _proven[s] = title;
        return _proven.TryGetValue(s, out var t) && t == title ? title : null;
    }

    private void Track(int? st, string? title)
    {
        if (st != _st) { _titleAtSwitch = _st is null ? null : title ?? ""; _st = st; }
    }
}

namespace SupaZero.Core.Game;

/// <summary>
/// v0.3.0 [V] snapshot 08:19:35 (25 Okt): Roll.CharacterSlots.Fade.List bisa KOSONG (hanya SlotTemp, menu Roll belum dibuka sejak join),
/// sementara Roll.CharacterInfo.Title tetap mengikuti unit terpilih di SkillTree (SkillTree Slot5 terpilih, Title "Twisted Agony").
/// Tanpa daftar Roll, slot judul tidak bisa dicocokkan langsung, jadi judul hanya diterima bila TERBUKTI berubah setelah slot SkillTree berganti
/// (judul saat pergantian ≠ judul sekarang). Judul yang tidak pernah berubah = basi/unknown → null (tidak menebak).
/// </summary>
public sealed class RollTitleTracker
{
    private int? _st;
    private string? _titleAtSwitch;
    private readonly Dictionary<int, string> _proven = new();

    public void Reset() { _st = null; _titleAtSwitch = null; _proven.Clear(); }

    /// <param name="st">slot terpilih di SkillTree</param>
    /// <param name="rollHasSlots">daftar Roll punya anak SlotN</param>
    /// <param name="rs">slot terpilih di daftar Roll</param>
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

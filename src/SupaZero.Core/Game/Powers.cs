using RIE.Core.Memory;
using System.Text.RegularExpressions;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

/// <summary>Trait satu power, dibaca dari layar Trait Reroll.</summary>
public sealed record PowerTrait(string Name, string? Chance, IReadOnlyList<string> Buffs);

/// <summary>Power yang dipasang di loadout (Slot 1/2).</summary>
public sealed record EquippedPower(int Loadout, int InventorySlot, string Name, string? CharacterId, int? Level, string? Rarity, PowerTrait? Trait);

/// <summary>
/// Pengetahuan power yang dipelajari saat jalan: id karakter internal → nama tampil, dan trait per nama tampil.
/// Disimpan ke settings agar tidak perlu dibaca ulang.
/// </summary>
public sealed class PowerKnowledge
{
    /// <summary>Terverifikasi RIE 23 Okt 2026 (animasi idle preview slot ↔ judul/CharacterSwitch).</summary>
    public static readonly IReadOnlyDictionary<string, string> BuiltInNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["shanks"] = "Divine Emperor",
        ["sukuna"] = "Cursed King",
        ["katakuri"] = "Mochi Commander",
        ["nagumo"] = "Kings Gambit",
    };

    private readonly object _gate = new();
    public Dictionary<string, string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PowerTrait> Traits { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Dirty { get; set; }
    /// <summary>Loadout terakhir yang terbaca (daftar slot hanya ada saat menu karakter pernah dibuka).</summary>
    public List<EquippedPower> Loadout { get; set; } = [];

    public void LearnLoadout(List<EquippedPower> loadout)
    {
        loadout = loadout.Select(p => p with { Trait = null }).ToList(); // trait disimpan terpisah (list Buffs tak bisa dibandingkan per nilai)
        lock (_gate)
        {
            if (Loadout.Select(p => p with { Trait = null }).SequenceEqual(loadout)) return;
            Loadout = loadout;
            Dirty = true;
        }
    }

    /// <summary>Loadout tersimpan dengan trait terbaru.</summary>
    public IReadOnlyList<EquippedPower> SavedLoadout()
    {
        lock (_gate) return Loadout.Select(p => p with { Trait = Traits.TryGetValue(p.Name, out var t) ? t : p.Trait }).ToList();
    }

    public string? NameFor(string? id)
    {
        if (id is null) return null;
        lock (_gate)
            return Names.TryGetValue(id, out var n) ? n : BuiltInNames.TryGetValue(id, out var b) ? b : null;
    }

    public void LearnName(string id, string name)
    {
        lock (_gate)
        {
            if (NameFor(id) == name) return;
            Names[id] = name;
            Dirty = true;
        }
    }

    public PowerTrait? TraitFor(string? name)
    {
        if (name is null) return null;
        lock (_gate) return Traits.TryGetValue(name, out var t) ? t : null;
    }

    public void LearnTrait(string name, PowerTrait trait)
    {
        lock (_gate)
        {
            if (Traits.TryGetValue(name, out var old) && old.Name == trait.Name && old.Chance == trait.Chance && old.Buffs.SequenceEqual(trait.Buffs)) return;
            Traits[name] = trait;
            Dirty = true;
        }
    }

    /// <summary>Format id internal ("sung_jinwoo" → "Sung Jinwoo") bila nama tampil belum diketahui.</summary>
    public static string Pretty(string id) =>
        string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}

public sealed partial class GameReader
{
    public PowerKnowledge Powers { get; set; } = new();

    // Terverifikasi RIE 23 Okt 2026, relatif PlayerGui.
    private const string InventoryList = "UI Animation (Finished).Roll.SurfaceGui.MainFrame.CharacterSlots.Fade.List";
    private const string SelectedTitle = "UI Animation (Finished).Roll.SurfaceGui.MainFrame.CharacterInfo.TitleShadow.Title";
    private const string TraitRoot = "UI Animation (Working).Trait Reroll.SurfaceGui.MainFrame.Left.Content";
    private const string TraitPowerName = "CharacterSwitch.CharacterName.Txt";
    private const string TraitCard = "RerollPage.CutRoll.OpenningCutFrame.RollFrameCut.RollFrame.TraitCard1.Hitbox";
    private const string TraitBuffs = "RerollPage.Buffs";
    private const string AnimCharacters = "assets.animations.characters";

    private static readonly string[] Rarities = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic", "Arcane", "Zenless", "Secret", "Exotic"];
    private static readonly Regex CounterRx = new(@"\s+\d+\s*/\s*\d+\s*$");
    private static readonly Regex SlotNumRx = new(@"(\d+)");
    private static readonly Regex TagRx = new(@"<[^>]*>");

    /// <summary>Teks GUI tanpa tag RichText (mis. "Cursed King &lt;font color=..&gt;2/4&lt;/font&gt;").</summary>
    private string? Plain(long node) => Text(node) is { } t ? TagRx.Replace(t, string.Empty).Trim() is { Length: > 0 } p ? p : null : null;

    /// <summary>Ringkasan pembacaan layar Trait terakhir (untuk log diagnosa).</summary>
    public string? TraitDebug { get; private set; }
    public string? PowerDebug { get; private set; }

    private Dictionary<string, string>? _idleToId;
    private DateTime _powersAt = DateTime.MinValue;
    private IReadOnlyList<EquippedPower> _equipped = [];

    /// <summary>Loadout + trait, dibaca paling sering tiap 2 detik (tanpa input apa pun).</summary>
    public IReadOnlyList<EquippedPower> ReadPowers(DataModelRoots roots)
    {
        if (DateTime.UtcNow - _powersAt < TimeSpan.FromSeconds(2)) return _equipped;
        _powersAt = DateTime.UtcNow;
        try
        {
            long pg = roots.PlayerGui;
            if (pg == 0) return _equipped = Powers.SavedLoadout();
            LearnFromTraitScreen(pg);
            var list = reader.ResolvePath(pg, InventoryList);
            if (list == 0) { PowerDebug = "Power: daftar slot tidak ada (buka menu karakter sekali)"; return _equipped = Powers.SavedLoadout(); }
            EnsureIdleMap(roots.ReplicatedStorage);

            var result = new List<EquippedPower>();
            int slots = 0;
            foreach (long slot in reader.GetChildren(list, 64))
            {
                string slotName = reader.GetName(slot);
                if (!slotName.StartsWith("Slot", StringComparison.Ordinal) || !int.TryParse(slotName[4..], out int inv)) continue;
                slots++;
                long main = reader.ResolvePath(slot, "MainButton.MainButton");
                string? id = CharacterId(slot);

                // Nama: belajar dari judul saat slot ini sedang dipilih.
                if (id is not null && main != 0 && IsVisibleFlag(Find(main, "SelectedFrame", 2)) && Text(reader.ResolvePath(pg, SelectedTitle)) is { Length: > 0 } title)
                    Powers.LearnName(id, title.Trim());

                long equipped = reader.ResolvePath(slot, "Options.EquipButton.EquipButton.Equipped");
                if (equipped == 0 || !IsVisibleFlag(equipped)) continue;
                var m = SlotNumRx.Match(Text(reader.FindChild(equipped, "Slot")) ?? string.Empty);
                if (!m.Success) continue;
                int loadout = int.Parse(m.Groups[1].Value);

                string name = Powers.NameFor(id) ?? (id is null ? $"Slot {inv}" : PowerKnowledge.Pretty(id));
                int? level = main != 0 ? ParseInt(Text(reader.ResolvePath(main, "LevelShadow.Level"))) : null;
                result.Add(new EquippedPower(loadout, inv, name, id, level, main != 0 ? Rarity(main) : null, Powers.TraitFor(name)));
            }
            PowerDebug = $"Power: {slots} slot terbaca, {result.Count} terpasang";
            if (result.Count > 0) Powers.LearnLoadout(result.OrderBy(p => p.Loadout).ToList());
            _equipped = Powers.SavedLoadout();
        }
        catch { /* baca memori gagal sesaat → pakai hasil sebelumnya */ }
        return _equipped;
    }

    private bool IsVisibleFlag(long node) =>
        node != 0 && reader.Offsets.TryResolve("GuiObject.Visible", out long vo) && M.TryReadByte(node + vo, out byte v) && v != 0;

    private string? Rarity(long main)
    {
        long r = reader.FindChild(main, "Rarities");
        if (r == 0) return null;
        foreach (long c in reader.GetChildren(r, 32))
        {
            string n = reader.GetName(c);
            if (n.EndsWith("Base", StringComparison.Ordinal) && IsVisibleFlag(c)) return n[..^4];
        }
        return Rarities.FirstOrDefault(x => reader.FindChild(r, x) is var c && c != 0 && IsVisibleFlag(c));
    }

    /// <summary>Peta AnimationId idle → id karakter dari ReplicatedStorage.assets.animations.characters.&lt;id&gt;.idle.</summary>
    private void EnsureIdleMap(long rs)
    {
        if (_idleToId is { Count: > 0 } || rs == 0) return;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        long chars = reader.ResolvePath(rs, AnimCharacters);
        foreach (long c in chars != 0 ? reader.GetChildren(chars, 256) : new List<long>())
        {
            long idle = reader.FindChild(c, "idle");
            if (idle != 0 && props.Read(idle).TryGetValue("AnimationId", out var a) && AssetNum(a) is { } num) map.TryAdd(num, reader.GetName(c));
        }
        _idleToId = map;
    }

    /// <summary>Animasi idle yang diputar model preview di kartu slot → id karakter.</summary>
    private string? CharacterId(long slot)
    {
        if (_idleToId is not { Count: > 0 }) return null;
        long animator = Find(slot, "Animator", 12, 400);
        if (animator == 0) return null;
        foreach (long t in props.GetActiveTracks(animator, 8))
            if (props.Read(t).TryGetValue("AnimationId", out var a) && AssetNum(a) is { } num && _idleToId.TryGetValue(num, out var id))
                return id;
        return null;
    }

    private static string? AssetNum(string s)
    {
        var m = Regex.Match(s, @"(\d{6,})");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Saat layar Trait Reroll terbuka: catat trait power yang sedang ditampilkan.</summary>
    private void LearnFromTraitScreen(long pg)
    {
        long root = reader.ResolvePath(pg, TraitRoot);
        if (root == 0 || !IsShown(root, pg)) return;
        string? header = Plain(reader.ResolvePath(root, TraitPowerName));
        if (header is null) { TraitDebug = "Trait: nama power kosong"; return; }
        string power = CounterRx.Replace(header, string.Empty).Trim();

        // Kartu trait aktif: TraitCard1, cadangan kartu lain yang terlihat dan berisi teks.
        long rollFrame = reader.ResolvePath(root, TraitCard[..TraitCard.LastIndexOf(".TraitCard1", StringComparison.Ordinal)]);
        string? trait = null, chance = null;
        foreach (int i in new[] { 1, 2, 3, 4, 5, 6, 7 })
        {
            long hit = rollFrame != 0 ? reader.ResolvePath(rollFrame, $"TraitCard{i}.Hitbox") : 0;
            if (hit == 0 || (i > 1 && !IsVisibleFlag(reader.ResolvePath(rollFrame, $"TraitCard{i}")))) continue;
            if (Plain(reader.FindChild(hit, "Txt")) is not { } t) continue;
            trait = t;
            long ch = reader.FindChild(hit, "Chance");
            chance = Plain(ch) ?? Plain(reader.FindChild(ch, "Txt"));
            break;
        }
        var buffs = new List<string>();
        long buffsNode = reader.ResolvePath(root, TraitBuffs);
        if (buffsNode != 0 && IsVisibleFlag(buffsNode))
            for (int i = 1; i <= 6; i++)
            {
                long b = reader.ResolvePath(buffsNode, $"Buff{i}.Buff{i}");
                if (b == 0 || !IsVisibleFlag(b)) continue;
                long sh = reader.FindChild(b, "Txt(Shadow)");
                if ((Plain(reader.FindChild(sh, "Txt")) ?? Plain(sh)) is { } v) buffs.Add(v);
            }
        TraitDebug = $"Trait: '{header}' → {power} = {trait ?? "(kosong)"} {chance} {string.Join(" ", buffs)}".TrimEnd();
        if (trait is null) return;
        Powers.LearnTrait(power, new PowerTrait(trait, chance, buffs));
    }
}

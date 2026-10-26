using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SupaZero.Core.Game;

/// <summary>Jenis buff kartu Mirror Blessing (Evil Reflection), diturunkan dari teks Description/Value kartu.</summary>
public enum BlessingBuff { Damage, MaxHealth, Heal, ExtraLife, MoveSpeed, Splash, TwinDamage, Other }

/// <summary>Data satu kartu Mirror Blessing: nama, rarity, teks game, buff.</summary>
public sealed record BlessingInfo(string Name, string Rarity, string Description, string Value)
{
    [JsonIgnore] public IReadOnlyList<BlessingBuff> Buffs => BlessingCatalog.Classify(Description + " " + Value);
    /// <summary>Kartu dengan efek negatif ("…, but take 20% more damage").</summary>
    [JsonIgnore] public bool HasDrawback => BlessingCatalog.IsDrawback(Description);
}

/// <summary>Urutan + aktif per buff (pengaturan).</summary>
public sealed record BuffPref(BlessingBuff Buff, bool Enabled);

/// <summary>Urutan + boleh dipilih per kartu (pengaturan). Enabled=false → jangan pilih bila ada pilihan lain.</summary>
public sealed record BlessingPref(string Name, bool Enabled);

public static class BlessingCatalog
{
    /// <summary>
    /// Kartu yang terbukti di game (PlayerGui.FateBlessings.Picker.Holder.List, snapshot RIE Byzan 24 Okt + 26 Okt 08:36:16/08:36:56/08:37:16).
    /// Value adalah contoh terbaca; angka bisa berbeda per tawaran (Sharper Edge COMMON terbaca +20% dan +35%).
    /// Kartu baru yang terbaca saat main otomatis ditambahkan ke FarmSettings.KnownBlessings.
    /// </summary>
    public static readonly IReadOnlyList<BlessingInfo> Seen =
    [
        new("Sharper Edge", "COMMON", "+35% damage", "+35%"),
        new("Big Heart", "COMMON", "+40% max health", "+40%"),
        new("Swift Steps", "COMMON", "+18% move speed", "+18%"),
        new("Chain Shatter", "RARE", "Reflections you shatter explode and hurt the ones nearby", "+50%"),
        new("Mirror Leech", "RARE", "Every hit heals 0.6% health (up to 4% a second)", "+0.6% a hit"),
        new("Shard Snack", "RARE", "", "+7.0% heal"),
        new("Glass Cannon", "EPIC", "+60% damage, but take 20% more damage", "+60%"),
        new("Twin Breaker", "EPIC", "+50% damage to your Evil Twin and its afterimages", "+50% vs twin"),
        new("Spare Heart", "EPIC", "+1 life", "+1 life"),
    ];

    public static readonly IReadOnlyList<BlessingBuff> AllBuffs = Enum.GetValues<BlessingBuff>();

    public static string Label(BlessingBuff b) => b switch
    {
        BlessingBuff.Damage => "Damage",
        BlessingBuff.MaxHealth => "Max health",
        BlessingBuff.Heal => "Heal / lifesteal",
        BlessingBuff.ExtraLife => "Extra life",
        BlessingBuff.MoveSpeed => "Move speed",
        BlessingBuff.Splash => "Ledakan area (splash)",
        BlessingBuff.TwinDamage => "Damage ke Evil Twin",
        _ => "Lainnya",
    };

    /// <summary>Kata kunci dari teks kartu yang terbaca. "twin" didahulukan: Twin Breaker hanya kena twin, bukan damage umum.</summary>
    public static IReadOnlyList<BlessingBuff> Classify(string text)
    {
        string t = text.ToLowerInvariant();
        var r = new List<BlessingBuff>();
        bool twin = t.Contains("twin");
        if (twin) r.Add(BlessingBuff.TwinDamage);
        if (!twin && Regex.IsMatch(t, @"\+\s*\d[\d.]*%\s*damage")) r.Add(BlessingBuff.Damage);
        if (t.Contains("max health")) r.Add(BlessingBuff.MaxHealth);
        if (Regex.IsMatch(t, @"\bheal(s|ing|ed)?\b")) r.Add(BlessingBuff.Heal); // bukan "health"
        if (Regex.IsMatch(t, @"\b(life|lives)\b")) r.Add(BlessingBuff.ExtraLife);
        if (t.Contains("move speed")) r.Add(BlessingBuff.MoveSpeed);
        if (t.Contains("explode") || t.Contains("nearby")) r.Add(BlessingBuff.Splash);
        if (r.Count == 0) r.Add(BlessingBuff.Other);
        return r;
    }

    public static bool IsDrawback(string description)
    {
        string t = description.ToLowerInvariant();
        return t.Contains("but take") || t.Contains("more damage");
    }

    /// <summary>Angka pertama di Value ("+35%" → 35, "+0.6% a hit" → 0.6). null bila tidak ada.</summary>
    public static double? Number(string? value)
    {
        if (value is null) return null;
        var m = Regex.Match(value, @"\d+(?:\.\d+)?");
        return m.Success ? double.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }

    public static int RarityRank(string r) => r.Trim().ToUpperInvariant() switch
    {
        "COMMON" => 1, "RARE" => 2, "EPIC" => 3, "LEGENDARY" => 4, "MYTHIC" => 5, _ => 0,
    };

    /// <summary>Gabungan kartu bawaan + yang dipelajari (nama sama → data terbaru dari game).</summary>
    public static List<BlessingInfo> All(IEnumerable<BlessingInfo>? learned)
    {
        var map = new Dictionary<string, BlessingInfo>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var b in Seen.Concat(learned ?? []))
        {
            if (!map.ContainsKey(b.Name)) order.Add(b.Name);
            map[b.Name] = b;
        }
        return order.Select(n => map[n]).ToList();
    }

    /// <summary>Urutan buff efektif: tersimpan + buff baru di belakang (mati).</summary>
    public static List<BuffPref> EffectiveBuffs(IReadOnlyList<BuffPref>? saved)
    {
        var r = (saved ?? []).GroupBy(b => b.Buff).Select(g => g.First()).ToList();
        foreach (var b in AllBuffs) if (r.All(x => x.Buff != b)) r.Add(new BuffPref(b, false));
        return r;
    }

    /// <summary>Urutan kartu efektif: tersimpan; bila belum ada, dari teks lama "a, b, c"; kartu lain menyusul (aktif).</summary>
    public static List<BlessingPref> EffectiveCards(IReadOnlyList<BlessingPref>? saved, string? legacyCsv, IEnumerable<BlessingInfo> all)
    {
        var r = new List<BlessingPref>();
        void Add(string n, bool en) { n = n.Trim(); if (n.Length > 0 && r.All(x => !x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) r.Add(new(n, en)); }
        if (saved is { Count: > 0 }) foreach (var p in saved) Add(p.Name, p.Enabled);
        else foreach (var n in (legacyCsv ?? "").Split(',')) Add(n, true);
        foreach (var b in all) Add(b.Name, true);
        return r;
    }

    /// <summary>
    /// Pilih kartu: (1) kartu yang dimatikan dilewati bila masih ada pilihan lain; (2) buff aktif teratas yang dimiliki kartu;
    /// (3) urutan kartu; (4) rarity tertinggi; (5) angka Value terbesar. Selalu mengembalikan satu kartu bila ada tawaran (timer game).
    /// </summary>
    public static BlessingOffer? Choose(IReadOnlyList<BlessingOffer> offers, IReadOnlyList<BuffPref> buffs, IReadOnlyList<BlessingPref> cards, IEnumerable<BlessingInfo>? known = null)
    {
        if (offers.Count == 0) return null;
        var info = All(known).ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        bool Off(BlessingOffer o) => cards.Any(c => !c.Enabled && Same(c.Name, o.Name));
        var pool = offers.Where(o => !Off(o)).ToList();
        if (pool.Count == 0) pool = offers.ToList();
        var activeBuffs = buffs.Where(b => b.Enabled).Select(b => b.Buff).ToList();
        int BuffRank(BlessingOffer o)
        {
            var bs = BuffsOf(o, info);
            int best = int.MaxValue;
            for (int i = 0; i < activeBuffs.Count; i++) if (bs.Contains(activeBuffs[i])) { best = i; break; }
            return best;
        }
        int CardRank(BlessingOffer o)
        {
            for (int i = 0; i < cards.Count; i++) if (cards[i].Enabled && Same(cards[i].Name, o.Name)) return i;
            return int.MaxValue;
        }
        return pool.OrderBy(BuffRank).ThenBy(CardRank)
            .ThenByDescending(o => RarityRank(o.Rarity)).ThenByDescending(o => Number(o.Value) ?? 0).First();
    }

    public static IReadOnlyList<BlessingBuff> BuffsOf(BlessingOffer o, IReadOnlyDictionary<string, BlessingInfo>? info = null)
    {
        if (o.Description is { Length: > 0 } || o.Value is { Length: > 0 })
        {
            var b = Classify((o.Description ?? "") + " " + (o.Value ?? ""));
            if (!(b.Count == 1 && b[0] == BlessingBuff.Other)) return b;
        }
        return info?.GetValueOrDefault(o.Name) is { } k ? k.Buffs : (Seen.FirstOrDefault(s => Same(s.Name, o.Name))?.Buffs ?? [BlessingBuff.Other]);
    }

    private static bool Same(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
}

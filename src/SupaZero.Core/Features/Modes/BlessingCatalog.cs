using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SupaZero.Core.Game;

/// <summary>Buff type of a Mirror Blessing card (Evil Reflection), derived from the card's Description/Value text.</summary>
public enum BlessingBuff { Damage, MaxHealth, Heal, ExtraLife, MoveSpeed, Splash, TwinDamage, Other }

/// <summary>Data for one Mirror Blessing card: name, rarity, game text, buffs.</summary>
public sealed record BlessingInfo(string Name, string Rarity, string Description, string Value)
{
    [JsonIgnore] public IReadOnlyList<BlessingBuff> Buffs => BlessingCatalog.Classify(Description + " " + Value);
    /// <summary>Card with a negative effect ("…, but take 20% more damage").</summary>
    [JsonIgnore] public bool HasDrawback => BlessingCatalog.IsDrawback(Description);
}

/// <summary>Order + enabled per buff (settings).</summary>
public sealed record BuffPref(BlessingBuff Buff, bool Enabled);

/// <summary>Order + pickable per card (settings). Enabled=false → do not pick if there is another option.</summary>
public sealed record BlessingPref(string Name, bool Enabled);

public static class BlessingCatalog
{
    /// <summary>
    /// Cards confirmed in game (PlayerGui.FateBlessings.Picker.Holder.List, Byzan RIE snapshots 24 Oct + 26 Oct 08:36:16/08:36:56/08:37:16).
    /// Value is an example as read; numbers can differ per offer (Sharper Edge COMMON read as +20% and +35%).
    /// New cards read during play are automatically added to FarmSettings.KnownBlessings.
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
        BlessingBuff.Splash => "Area explosion (splash)",
        BlessingBuff.TwinDamage => "Damage vs Evil Twin",
        _ => "Other",
    };

    /// <summary>Keywords from the read card text. "twin" takes precedence: Twin Breaker only affects the twin, not general damage.</summary>
    public static IReadOnlyList<BlessingBuff> Classify(string text)
    {
        string t = text.ToLowerInvariant();
        var r = new List<BlessingBuff>();
        bool twin = t.Contains("twin");
        if (twin) r.Add(BlessingBuff.TwinDamage);
        if (!twin && Regex.IsMatch(t, @"\+\s*\d[\d.]*%\s*damage")) r.Add(BlessingBuff.Damage);
        if (t.Contains("max health")) r.Add(BlessingBuff.MaxHealth);
        if (Regex.IsMatch(t, @"\bheal(s|ing|ed)?\b")) r.Add(BlessingBuff.Heal); // not "health"
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

    /// <summary>First number in Value ("+35%" → 35, "+0.6% a hit" → 0.6). null if none.</summary>
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

    /// <summary>Built-in cards merged with learned ones (same name → latest data from the game).</summary>
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

    /// <summary>Effective buff order: saved + new buffs appended (disabled).</summary>
    public static List<BuffPref> EffectiveBuffs(IReadOnlyList<BuffPref>? saved)
    {
        var r = (saved ?? []).GroupBy(b => b.Buff).Select(g => g.First()).ToList();
        foreach (var b in AllBuffs) if (r.All(x => x.Buff != b)) r.Add(new BuffPref(b, false));
        return r;
    }

    /// <summary>Effective card order: saved; if none, from the legacy text "a, b, c"; other cards follow (enabled).</summary>
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
    /// Pick a card: (1) disabled cards are skipped while other options exist; (2) highest-ranked enabled buff the card has;
    /// (3) card order; (4) highest rarity; (5) largest Value number. Always returns one card when there are offers (game timer).
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

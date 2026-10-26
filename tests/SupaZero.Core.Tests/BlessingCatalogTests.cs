using SupaZero.Core.Game;
using SupaZero.Core.Settings;
using Xunit;

namespace SupaZero.Core.Tests;

/// <summary>v0.3.11: real offers from Byzan's PlayerGui snapshot 26 Oct 2026 (FateBlessings.Picker.Holder.List.OfferN.Body).</summary>
public class BlessingCatalogTests
{
    private static BlessingOffer O(string n, string r, string d, string v, int x) => new(n, r, v, new GuiRect(x, 774, 352, 49), d);

    // 08:36:16
    private static readonly List<BlessingOffer> S0836a =
    [
        O("Glass Cannon", "EPIC", "+60% damage, but take 20% more damage", "+60%", 390),
        O("Twin Breaker", "EPIC", "+50% damage to your Evil Twin and its afterimages", "+50% vs twin", 787),
        O("Mirror Leech", "RARE", "Every hit heals 0.6% health (up to 4% a second)", "+0.6% a hit", 1181),
    ];
    // 08:36:56
    private static readonly List<BlessingOffer> S0836b =
    [
        O("Sharper Edge", "COMMON", "+35% damage", "+35%", 390),
        O("Big Heart", "COMMON", "+40% max health", "+40%", 787),
        O("Spare Heart", "EPIC", "+1 life", "+1 life", 1181),
    ];
    // 08:37:16
    private static readonly List<BlessingOffer> S0837 =
    [
        O("Swift Steps", "COMMON", "+18% move speed", "+18%", 390),
        O("Mirror Leech", "RARE", "Every hit heals 0.6% health (up to 4% a second)", "+0.6% a hit", 787),
        O("Glass Cannon", "EPIC", "+60% damage, but take 20% more damage", "+60%", 1181),
    ];

    [Theory]
    [InlineData("+60% damage, but take 20% more damage", BlessingBuff.Damage)]
    [InlineData("+50% damage to your Evil Twin and its afterimages", BlessingBuff.TwinDamage)]
    [InlineData("Every hit heals 0.6% health (up to 4% a second)", BlessingBuff.Heal)]
    [InlineData("+40% max health", BlessingBuff.MaxHealth)]
    [InlineData("+1 life", BlessingBuff.ExtraLife)]
    [InlineData("+18% move speed", BlessingBuff.MoveSpeed)]
    [InlineData("Reflections you shatter explode and hurt the ones nearby", BlessingBuff.Splash)]
    public void Classify_FromGameText(string text, BlessingBuff want) => Assert.Equal(want, Assert.Single(BlessingCatalog.Classify(text)));

    [Fact]
    public void Catalog_HasAllSeenCards_WithDrawbackOnlyOnGlassCannon()
    {
        Assert.Equal(9, BlessingCatalog.Seen.Count);
        Assert.Equal(["Glass Cannon"], BlessingCatalog.Seen.Where(b => b.HasDrawback).Select(b => b.Name));
        Assert.Contains(BlessingBuff.Heal, BlessingCatalog.Seen.Single(b => b.Name == "Shard Snack").Buffs);
    }

    private static List<BuffPref> Buffs(params BlessingBuff[] on) =>
        BlessingCatalog.EffectiveBuffs(on.Select(b => new BuffPref(b, true)).ToList());

    [Fact]
    public void BuffPriority_PicksCardWithTopBuff()
    {
        var cards = BlessingCatalog.EffectiveCards(null, "", BlessingCatalog.Seen);
        Assert.Equal("Spare Heart", BlessingCatalog.Choose(S0836b, Buffs(BlessingBuff.ExtraLife, BlessingBuff.Damage), cards)!.Name);
        Assert.Equal("Sharper Edge", BlessingCatalog.Choose(S0836b, Buffs(BlessingBuff.Damage, BlessingBuff.ExtraLife), cards)!.Name);
        Assert.Equal("Big Heart", BlessingCatalog.Choose(S0836b, Buffs(BlessingBuff.MaxHealth), cards)!.Name);
        Assert.Equal("Mirror Leech", BlessingCatalog.Choose(S0837, Buffs(BlessingBuff.Heal, BlessingBuff.Damage), cards)!.Name);
        // buff not in the offers → next buff
        Assert.Equal("Glass Cannon", BlessingCatalog.Choose(S0837, Buffs(BlessingBuff.TwinDamage, BlessingBuff.Damage), cards)!.Name);
    }

    [Fact]
    public void DisabledCard_IsSkipped_UnlessAllDisabled()
    {
        var cards = BlessingCatalog.EffectiveCards([new("Glass Cannon", false)], "", BlessingCatalog.Seen);
        Assert.Equal("Swift Steps", BlessingCatalog.Choose(S0837, Buffs(BlessingBuff.Damage, BlessingBuff.MoveSpeed), cards)!.Name);
        var none = S0837.Select(o => new BlessingPref(o.Name, false)).ToList();
        Assert.NotNull(BlessingCatalog.Choose(S0837, [], none)); // game timer: still pick one
    }

    [Fact]
    public void NoBuffs_FollowsCardOrder_ThenRarity()
    {
        var cards = BlessingCatalog.EffectiveCards(null, "Twin Breaker, Mirror Leech", BlessingCatalog.Seen);
        Assert.Equal("Twin Breaker", BlessingCatalog.Choose(S0836a, Buffs(), cards)!.Name);
        Assert.Equal("Glass Cannon", BlessingCatalog.Choose(S0836a, [], [])!.Name); // EPIC, +60 > +50
    }

    [Fact]
    public void LegacyCsv_MigratesToOrderedList_WithRestOfCatalog()
    {
        var f = new FarmSettings();
        var l = BlessingCatalog.EffectiveCards(f.BlessingCards, f.BlessingPriority, BlessingCatalog.All(f.KnownBlessings));
        Assert.Equal(["Sharper Edge", "Shard Snack", "Big Heart", "Chain Shatter", "Swift Steps"], l.Take(5).Select(x => x.Name));
        Assert.Equal(9, l.Count);
        Assert.All(l, x => Assert.True(x.Enabled));
    }

    [Fact]
    public void LearnedCard_JoinsCatalog_AndRoundTripsJson()
    {
        var learned = new List<BlessingInfo> { new("New Card", "LEGENDARY", "+10% move speed", "+10%") };
        var all = BlessingCatalog.All(learned);
        Assert.Equal(10, all.Count);
        var json = System.Text.Json.JsonSerializer.Serialize(new FarmSettings { KnownBlessings = learned, BlessingBuffs = Buffs(BlessingBuff.Heal) });
        var back = System.Text.Json.JsonSerializer.Deserialize<FarmSettings>(json)!;
        Assert.Equal(learned[0], back.KnownBlessings![0]);
        Assert.True(back.BlessingBuffs![0].Enabled);
    }
}

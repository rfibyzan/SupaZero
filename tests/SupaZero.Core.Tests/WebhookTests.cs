using System.Text.Json;
using SupaZero.Core.Notify;

namespace SupaZero.Core.Tests;

public class WebhookTests
{
    [Theory]
    [InlineData("https://discord.com/api/webhooks/1/abc", true)]
    [InlineData("https://discordapp.com/api/webhooks/1/abc", true)]
    [InlineData("http://discord.com/api/webhooks/1/abc", false)]
    [InlineData("https://evil.com/api/webhooks/1/abc", false)]
    [InlineData("", false)]
    public void ValidatesUrl(string url, bool ok) => Assert.Equal(ok, DiscordWebhook.IsValidUrl(url));

    [Fact]
    public void RoundEmbed_HasStageAndDrops()
    {
        var e = DiscordWebhook.ForRound(new RoundReport(true, "Leaf Village", 3, "Hard", TimeSpan.FromSeconds(95), 4, "VICTORY", ["Gem x50", "Gold x1200"]));
        Assert.Contains(e.Fields, f => f.Name == "Stage" && f.Value == "Leaf Village · Act 3");
        Assert.Contains(e.Fields, f => f.Name == "Drop" && f.Value == "Gem x50\nGold x1200");
        Assert.Contains(e.Fields, f => f.Name == "Time" && f.Value == "1:35");
        Assert.Contains("\"embeds\"", JsonSerializer.Serialize(new DiscordWebhook.Payload(null, "SupaZero", [e])));
        Assert.Equal(DiscordWebhook.Green, e.Color);
    }

    [Fact]
    public void Stamp_AddsAccountName()
    {
        var e = DiscordWebhook.Stamp(new DiscordWebhook.Embed("Victory", "VICTORY!", DiscordWebhook.Green, []), "rfibyzan");
        Assert.Equal("Account: rfibyzan", e.Author?.Name);
        Assert.Equal("SupaZero · Anime Zero · rfibyzan", e.Footer?.Text);
        Assert.Contains("\"author\":{\"name\":\"Account: rfibyzan\"}", JsonSerializer.Serialize(e));
        var none = DiscordWebhook.Stamp(new DiscordWebhook.Embed("Victory", null, DiscordWebhook.Green, []), "");
        Assert.Null(none.Author);
        Assert.Equal("SupaZero · Anime Zero", none.Footer?.Text);
    }
}

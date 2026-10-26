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
}

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SupaZero.Core.Notify;

/// <summary>End-of-round report for the webhook.</summary>
public sealed record RoundReport(bool Victory, string Map, int Act, string? Difficulty, TimeSpan? Duration, int StagesCleared,
    string? ResultText, IReadOnlyList<string> Drops, bool Infinite = false);

/// <summary>Send an embed to a Discord webhook (HTTP POST from the app; does not touch Roblox).</summary>
public static class DiscordWebhook
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static bool IsValidUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && (u.Host is "discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com")
        && u.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);

    public const int Green = 0x2ECC71, Red = 0xE74C3C, Gray = 0x95A5A6, Blue = 0x3498DB;

    public static Embed ForRound(RoundReport r)
    {
        var fields = new List<Field>();
        string stage = r.Map.Length > 0 ? $"{r.Map}{(r.Act > 0 ? $" · Act {r.Act}" : "")}" : "-";
        fields.Add(new("Stage", stage, true));
        if (r.Difficulty is { Length: > 0 } d) fields.Add(new("Difficulty", d, true));
        if (r.Duration is { } t) fields.Add(new("Time", t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"), true));
        fields.Add(new("Stages cleared", r.StagesCleared.ToString(), true));
        fields.Add(new("Drop", r.Drops.Count > 0 ? string.Join("\n", r.Drops.Take(25)) : "(not read yet)", false));
        // Infinite has no "win": the round ends when the character dies, EndScreen shows "WAVE n".
        if (r.Infinite) return new("Infinite finished (character died)", r.ResultText, Blue, fields);
        return new(r.Victory ? "Victory" : "Defeat", r.ResultText, r.Victory ? Green : Red, fields);
    }

    public const int Gold = 0xF1C40F, Purple = 0x9B59B6;

    public static Embed ForSpin(SupaZero.Core.Game.SpinReport r)
    {
        var f = new List<Field>();
        static string Dur(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        if (r.IsTrait)
        {
            if (r.Power is { Length: > 0 } pw) f.Add(new("Power", pw, true));
            f.Add(new("Trait", r.Result ?? "(not read yet)", true));
            if (r.Chance is { Length: > 0 } c) f.Add(new("Chance", c, true));
            if (r.Buffs.Count > 0) f.Add(new("Buff", string.Join(" ", r.Buffs), true));
            if (r.Used is { } tu) f.Add(new("Dice used", tu.ToString(), true));
            if (r.LeftAfter is { } tl) f.Add(new("Dice left", tl.ToString(), true));
            f.Add(new("Duration", Dur(r.Duration), true));
            return new(r.Title, null, Purple, f);
        }
        f.Add(new("Last result", r.Result is null ? "(not read yet)" : r.Rarity is { } ra ? $"{r.Result} ({ra})" : r.Result, true));
        if (r.Mode is { Length: > 0 } m) f.Add(new("Mode", m, true));
        if (r.StopAt is { Length: > 0 } st) f.Add(new("Stop at", st, true));
        if (r.Spins is { } n) f.Add(new("Spin", n.ToString(), true));
        if (r.Used is { } u) f.Add(new("Used", u.ToString(), true));
        if (r.LeftAfter is { } l) f.Add(new("Left", l.ToString(), true));
        f.Add(new("Duration", Dur(r.Duration), true));
        if (r.Pity.Count > 0) f.Add(new("Pity", string.Join("\n", r.Pity), false));
        return new(r.Title, null, r.StoppedOnTarget ? Gold : Blue, f);
    }

    /// <summary>v0.3.50 (Byzan 28 Oct): the Roblox account the notification is for, shown as the embed author (top line) and in the footer.</summary>
    public static Embed Stamp(Embed embed, string? account)
    {
        string? acc = string.IsNullOrWhiteSpace(account) ? null : account.Trim();
        return embed with
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("o"),
            Author = acc is null ? null : new("Account: " + acc),
            Footer = new(acc is null ? "SupaZero · Anime Zero" : $"SupaZero · Anime Zero · {acc}"),
        };
    }

    public static async Task<string?> SendAsync(string url, Embed embed, string? mention = null, string? account = null, CancellationToken ct = default)
    {
        if (!IsValidUrl(url)) return "Invalid webhook URL (must be https://discord.com/api/webhooks/...).";
        var payload = new Payload(
            string.IsNullOrWhiteSpace(mention) ? null : mention.Trim(),
            "SupaZero",
            [Stamp(embed, account)]);
        try
        {
            using var res = await Http.PostAsJsonAsync(url.Trim(), payload, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode ? null : $"Discord rejected the request ({(int)res.StatusCode}).";
        }
        catch (Exception ex) { return "Failed to send webhook: " + ex.Message; }
    }

    public sealed record Payload(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("embeds")] IReadOnlyList<Embed> Embeds);

    public sealed record Embed(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
        [property: JsonPropertyName("color")] int Color,
        [property: JsonPropertyName("fields")] IReadOnlyList<Field> Fields)
    {
        [JsonPropertyName("timestamp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Timestamp { get; init; }
        [JsonPropertyName("footer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Footer? Footer { get; init; }
        [JsonPropertyName("author"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Author? Author { get; init; }
    }

    public sealed record Author([property: JsonPropertyName("name")] string Name);

    public sealed record Field(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("inline")] bool Inline);

    public sealed record Footer([property: JsonPropertyName("text")] string Text);
}

namespace SupaZero.Core.Settings;

/// <summary>Discord webhook: win/loss notifications (with drops) and macro stopped.</summary>
public sealed record WebhookSettings(string Url = "", bool OnVictory = true, bool OnDefeat = true, bool OnStop = true, string Mention = "", bool OnSpin = true);

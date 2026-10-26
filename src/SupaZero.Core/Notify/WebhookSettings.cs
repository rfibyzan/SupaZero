namespace SupaZero.Core.Settings;

/// <summary>Discord webhook: notifikasi menang/kalah (dengan drop) dan macro berhenti.</summary>
public sealed record WebhookSettings(string Url = "", bool OnVictory = true, bool OnDefeat = true, bool OnStop = true, string Mention = "", bool OnSpin = true);

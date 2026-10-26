namespace SupaZero.Core.Settings;

public enum UpgradeMode
{
    /// <summary>Habiskan kolom prioritas pertama dulu, baru kolom berikutnya.</summary>
    Priority,
    /// <summary>Beli bergiliran satu slot per kolom mengikuti urutan prioritas.</summary>
    RoundRobin,
}

public sealed record UpgradeSettings(
    bool Enabled = false,
    UpgradeMode Mode = UpgradeMode.Priority,
    List<string>? Priority = null,
    int MaxBuysPerStage = 5,
    /// <summary>Usang sejak v0.3.3 (diganti TargetSlot), dibiarkan agar settings lama tetap terbaca.</summary>
    string? OnlyUnit = null,
    bool DryRun = false,
    List<string>? EnabledColumns = null,
    /// <summary>v0.3.3: slot unit di daftar SkillTree (1–15, urutan sama dengan Trait/Power Spin). 0 = unit yang sedang terpilih.</summary>
    int TargetSlot = 0)
{
    /// <summary>v0.2.96: kolom yang boleh di-upgrade (toggle per kolom). null = semua (perilaku lama).</summary>
    public IReadOnlyList<string> ActiveColumns =>
        new[] { "ATK", "CRIT", "SPD", "DEF" }.Where(c => EnabledColumns is null || EnabledColumns.Contains(c)).ToList();
    public static IReadOnlyList<string> Columns { get; } = ["ATK", "CRIT", "DEF", "SPD"];
    public IReadOnlyList<string> EffectivePriority =>
        Priority is { Count: > 0 } p ? p : ["ATK", "CRIT", "SPD", "DEF"];
}

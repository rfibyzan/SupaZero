namespace SupaZero.Core.Settings;

public enum UpgradeMode
{
    /// <summary>Exhaust the first priority column before moving to the next.</summary>
    Priority,
    /// <summary>Buy one slot per column in turn, following priority order.</summary>
    RoundRobin,
}

public sealed record UpgradeSettings(
    bool Enabled = false,
    UpgradeMode Mode = UpgradeMode.Priority,
    List<string>? Priority = null,
    int MaxBuysPerStage = 5,
    /// <summary>Obsolete since v0.3.3 (replaced by TargetSlot), kept so old settings still load.</summary>
    string? OnlyUnit = null,
    bool DryRun = false,
    List<string>? EnabledColumns = null,
    /// <summary>v0.3.3: unit slot in the SkillTree list (1–15, same order as Trait/Power Spin). 0 = currently selected unit.</summary>
    int TargetSlot = 0)
{
    /// <summary>v0.2.96: columns allowed to be upgraded (per-column toggle). null = all (legacy behavior).</summary>
    public IReadOnlyList<string> ActiveColumns =>
        new[] { "ATK", "CRIT", "SPD", "DEF" }.Where(c => EnabledColumns is null || EnabledColumns.Contains(c)).ToList();
    public static IReadOnlyList<string> Columns { get; } = ["ATK", "CRIT", "DEF", "SPD"];
    public IReadOnlyList<string> EffectivePriority =>
        Priority is { Count: > 0 } p ? p : ["ATK", "CRIT", "SPD", "DEF"];
}

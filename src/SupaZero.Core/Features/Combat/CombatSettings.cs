namespace SupaZero.Core.Settings;

/// <summary>One skill key: key (HotkeyMap name, e.g. "1", "Q"), enabled, delay between presses.</summary>
public sealed record SkillBinding(string Name, string Key, bool Enabled, int CooldownMs);

public sealed record CombatSettings(
    int AttackIntervalMs = 150,
    List<SkillBinding>? Skills = null,
    /// <summary>v0.2.67: Shadow Lord: approach a corpse with an ARISE prompt, then hold E until it rises as an ally.</summary>
    bool AutoArise = true,
    /// <summary>v0.2.72: turn shift lock on automatically during a round; turned off when GUI clicks are needed / outside a round.</summary>
    bool AutoShiftLock = true,
    /// <summary>v0.3.21 (Byzan 27 Oct): Ultimate (G) only while a boss is fighting: PlayerGui.BossBar shown and not [IMMUNE].</summary>
    bool UltBossOnly = true,
    /// <summary>v0.3.23 Carry (alt account): once this player's scoreboard damage reaches this value, stop fighting and idle until the
    /// round ends (Retry is handled by the end screen as usual). 0 = off.</summary>
    int CarryDamage = 0,
    /// <summary>v0.3.32 (Byzan 27 Oct 23:26): app mode switch Normal/Carry in the title bar (like RIE expert/newbie). Carry damage,
    /// carry windows and rotation only apply in Carry mode; Normal mode always fights normally on one window.</summary>
    bool CarryMode = false)
{
    /// <summary>Damage target that is actually in effect: CarryDamage in Carry mode, 0 (= off) in Normal mode.</summary>
    public int CarryTarget => CarryMode ? CarryDamage : 0;

    public static List<SkillBinding> DefaultSkills() =>
    [
        new("Skill 1", "1", true, 4000),
        new("Skill 2", "2", true, 6000),
        new("Skill 3", "3", true, 8000),
        new("Dash", "Q", true, 3000),
        new("Swap power", "T", true, 15000),
        new("Ultimate", "G", true, 1500),
    ];

    /// <summary>v0.2.65: old settings without an Ultimate (G) row get it added automatically.</summary>
    public IReadOnlyList<SkillBinding> EffectiveSkills => Skills is { Count: > 0 } s
        ? (s.Any(k => k.Name == "Ultimate") ? s : [.. s, DefaultSkills().First(k => k.Name == "Ultimate")])
        : DefaultSkills();
}

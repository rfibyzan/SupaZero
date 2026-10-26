namespace SupaZero.Core.Settings;

/// <summary>Satu tombol skill: tombol (nama HotkeyMap, mis. "1", "Q"), aktif, jeda antar-tekan.</summary>
public sealed record SkillBinding(string Name, string Key, bool Enabled, int CooldownMs);

public sealed record CombatSettings(
    int AttackIntervalMs = 150,
    List<SkillBinding>? Skills = null,
    /// <summary>v0.2.67: Shadow Lord: dekati mayat berprompt ARISE lalu tahan E sampai bangkit jadi sekutu.</summary>
    bool AutoArise = true,
    /// <summary>v0.2.72: nyalakan shift lock otomatis selama ronde; dimatikan saat perlu klik GUI / di luar ronde.</summary>
    bool AutoShiftLock = true)
{
    public static List<SkillBinding> DefaultSkills() =>
    [
        new("Skill 1", "1", true, 4000),
        new("Skill 2", "2", true, 6000),
        new("Skill 3", "3", true, 8000),
        new("Dash", "Q", true, 3000),
        new("Ganti power", "T", true, 15000),
        new("Ultimate", "G", true, 1500),
    ];

    /// <summary>v0.2.65: pengaturan lama tanpa baris Ultimate (G) otomatis ditambah.</summary>
    public IReadOnlyList<SkillBinding> EffectiveSkills => Skills is { Count: > 0 } s
        ? (s.Any(k => k.Name == "Ultimate") ? s : [.. s, DefaultSkills().First(k => k.Name == "Ultimate")])
        : DefaultSkills();
}

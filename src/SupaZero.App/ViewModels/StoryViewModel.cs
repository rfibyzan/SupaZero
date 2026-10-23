using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

public sealed class StoryViewModel(MainViewModel root) : ObservableObject
{
    private AppSettings S => root.Settings;

    public IReadOnlyList<string> Acts { get; } = ["Act 1", "Act 2", "Act 3"];
    public IReadOnlyList<string> Difficulties { get; } = Enum.GetNames<Difficulty>();

    private void Story(Func<StorySettings, StorySettings> f) { S.Story = f(S.Story); root.Save(); }
    private void Combat(Func<CombatSettings, CombatSettings> f) { S.Combat = f(S.Combat); root.Save(); }

    public string Map { get => S.Story.Map; set => Story(s => s with { Map = value?.Trim() ?? string.Empty }); }
    public int ActIndex { get => Math.Clamp(S.Story.Act - 1, 0, 2); set => Story(s => s with { Act = Math.Clamp(value, 0, 2) + 1 }); }
    public int DifficultyIndex { get => (int)S.Story.Difficulty; set => Story(s => s with { Difficulty = (Difficulty)Math.Clamp(value, 0, 3) }); }
    public bool AutoCreateLobby { get => S.Story.AutoCreateLobby; set => Story(s => s with { AutoCreateLobby = value }); }
    public bool AutoVoteStart { get => S.Story.AutoVoteStart; set => Story(s => s with { AutoVoteStart = value }); }
    public bool AutoNext { get => S.Story.AutoNext; set => Story(s => s with { AutoNext = value }); }
    public bool RetryOnDefeat { get => S.Story.RetryOnDefeat; set => Story(s => s with { RetryOnDefeat = value }); }
    public decimal MaxRetries { get => S.Story.MaxRetriesPerStage; set => Story(s => s with { MaxRetriesPerStage = (int)value }); }

    public decimal AttackIntervalMs { get => S.Combat.AttackIntervalMs; set => Combat(c => c with { AttackIntervalMs = (int)value }); }
    public bool UseSkills { get => S.Combat.UseSkills; set => Combat(c => c with { UseSkills = value }); }
    public string SkillKeys { get => S.Combat.SkillKeys; set => Combat(c => c with { SkillKeys = value ?? string.Empty }); }
    public decimal SkillIntervalMs { get => S.Combat.SkillIntervalMs; set => Combat(c => c with { SkillIntervalMs = (int)value }); }
}

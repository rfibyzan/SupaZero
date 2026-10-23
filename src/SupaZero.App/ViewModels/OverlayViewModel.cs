using SupaZero.Core.Automation;
using SupaZero.Core.Game;

namespace SupaZero.App.ViewModels;

/// <summary>Isi overlay selama macro berjalan (diperbarui tiap tick timer UI, 500 ms).</summary>
public sealed class OverlayViewModel(MainViewModel root) : ObservableObject
{
    private string _runTime = "", _phase = "", _status = "", _stage = "", _area = "", _target = "", _skills = "", _power = "", _score = "", _hint = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;

    public string RunTime { get => _runTime; private set => Set(ref _runTime, value); }
    public string Phase { get => _phase; private set => Set(ref _phase, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Stage { get => _stage; private set => Set(ref _stage, value); }
    public string Area { get => _area; private set => Set(ref _area, value); }
    public string Target { get => _target; private set => Set(ref _target, value); }
    public string Skills { get => _skills; private set => Set(ref _skills, value); }
    public string Power { get => _power; private set => Set(ref _power, value); }
    public string Score { get => _score; private set => Set(ref _score, value); }
    public string Hint { get => _hint; private set => Set(ref _hint, value); }

    public void Refresh(GameSnapshot s)
    {
        var m = root.Runner.Machine;
        var t = DateTime.Now - StartedAt;
        RunTime = t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        Phase = PhaseText(m.Phase);
        Status = m.Status;

        var f = root.Settings.Farm;
        string map = m.CurrentMap.Length > 0 ? m.CurrentMap : f.Map.Length > 0 ? f.Map : "-";
        int act = m.CurrentAct > 0 ? m.CurrentAct : f.Act;
        Stage = $"{f.Mode} · {map}{(act > 0 ? $" · Act {act}" : "")} · {s.Hud?.Difficulty ?? f.Difficulty.ToString()}";

        Area = s.Screen == GameScreen.InRound && s.Hud is { } h
            ? $"{(s.RunWave is { Length: > 0 } w ? w + " · " : "")}Area {h.Area}/{h.AreaTotal} · {(s.EnemyCount >= 0 ? s.EnemyCount : h.EnemiesLeft)} musuh{(s.BossImmune ? " · boss IMMUNE" : "")}"
            : ScreenText(s.Screen);

        Target = "-";
        if (s.Screen == GameScreen.InRound && s.PlayerPos is { } p
            && FarmMachine.Targets(s).OrderBy(e => Navigator.Distance2D(p.X, p.Z, e.Pos.X, e.Pos.Z)).FirstOrDefault() is { } e)
        {
            double d = Navigator.Distance2D(p.X, p.Z, e.Pos.X, e.Pos.Z);
            int hp = e.MaxHealth > 0 ? (int)Math.Round(100 * e.Health / e.MaxHealth) : 0;
            Target = $"#{e.Name} · {d:0} stud (jaga {root.Settings.Navigation.AttackRange:0}) · HP {hp}%";
        }

        var parts = s.SkillCooldowns.Select(c => c.Locked ? $"{c.Key} 🔒" : c.Remaining is double r && r > 0 ? $"{c.Key} {r:0.#}s" : $"{c.Key} ✓").ToList();
        parts.Add(s.DashCooling ? "Q …" : "Q ✓");
        Skills = string.Join("   ", parts);

        Power = s.Equipped.Count > 0 ? string.Join("  |  ", s.Equipped.Select(x => x.Name)) : "-";
        Score = $"Menang {m.StagesCleared} · Kalah {m.Defeats} · Retry {m.Retries}";
        Hint = $"{root.Settings.General.MacroHotkey} untuk stop";
    }

    private static string PhaseText(MacroPhase p) => p switch
    {
        MacroPhase.WalkingToLobby => "Ke lingkaran room",
        MacroPhase.Lobby => "Menu Create Room",
        MacroPhase.Voting => "Vote start",
        MacroPhase.Fighting => "Bertarung",
        MacroPhase.Navigating => "Ke area berikutnya",
        MacroPhase.Upgrading => "Upgrade SkillTree",
        MacroPhase.Advancing => "Lanjut stage",
        MacroPhase.Retrying => "Retry",
        MacroPhase.Done => "Selesai",
        _ => "Menunggu",
    };

    private static string ScreenText(GameScreen s) => s switch
    {
        GameScreen.Lobby => "Lobby",
        GameScreen.LobbyMenu => "Menu Create Room",
        GameScreen.QueueWaiting => "Menunggu room",
        GameScreen.StartVote => "Vote start",
        GameScreen.Victory => "Menang",
        GameScreen.Defeat => "Kalah",
        GameScreen.InRound => "Dalam stage",
        GameScreen.BlessingPick => "Pilih Mirror Blessing",
        GameScreen.TrialsMenu => "Panel Trials",
        _ => "Menunggu Roblox",
    };
}

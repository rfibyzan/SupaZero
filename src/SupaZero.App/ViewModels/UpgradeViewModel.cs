using System.Collections.ObjectModel;
using SupaZero.Core.Settings;

namespace SupaZero.App.ViewModels;

public sealed class PriorityItemViewModel(string name, UpgradeViewModel owner)
{
    public string Name { get; } = name;
    public RelayCommand UpCommand => new(_ => owner.Move(this, -1));
    public RelayCommand DownCommand => new(_ => owner.Move(this, +1));
}

public sealed class UpgradeViewModel : ObservableObject
{
    private readonly MainViewModel _root;

    public UpgradeViewModel(MainViewModel root)
    {
        _root = root;
        var order = root.Settings.Upgrade.EffectivePriority.Concat(UpgradeSettings.Columns).Distinct();
        foreach (var c in order) Priority.Add(new PriorityItemViewModel(c, this));
    }

    private AppSettings S => _root.Settings;
    private void Up(Func<UpgradeSettings, UpgradeSettings> f) { S.Upgrade = f(S.Upgrade); _root.Save(); }

    public ObservableCollection<PriorityItemViewModel> Priority { get; } = [];
    public IReadOnlyList<string> Modes { get; } = ["Prioritas", "Bergiliran"];

    public bool Enabled { get => S.Upgrade.Enabled; set => Up(u => u with { Enabled = value }); }
    public int ModeIndex { get => (int)S.Upgrade.Mode; set => Up(u => u with { Mode = (UpgradeMode)Math.Clamp(value, 0, 1) }); }
    public bool DryRun { get => S.Upgrade.DryRun; set => Up(u => u with { DryRun = value }); }
    public IReadOnlyList<string> TargetSlots { get; } = ["Unit terpilih", .. Enumerable.Range(1, 15).Select(i => $"Slot {i}")];
    public int TargetSlotIndex { get => Math.Clamp(S.Upgrade.TargetSlot, 0, 15); set { if (value >= 0) Up(u => u with { TargetSlot = Math.Clamp(value, 0, 15) }); } }
    private bool Col(string c) => S.Upgrade.EnabledColumns is null || S.Upgrade.EnabledColumns.Contains(c);
    private void SetCol(string c, bool on) => Up(u =>
    {
        var set = (u.EnabledColumns ?? [.. UpgradeSettings.Columns]).ToHashSet();
        if (on) set.Add(c); else set.Remove(c);
        return u with { EnabledColumns = UpgradeSettings.Columns.Where(set.Contains).ToList() };
    });
    public bool Atk { get => Col("ATK"); set => SetCol("ATK", value); }
    public bool Crit { get => Col("CRIT"); set => SetCol("CRIT", value); }
    public bool Spd { get => Col("SPD"); set => SetCol("SPD", value); }
    public bool Def { get => Col("DEF"); set => SetCol("DEF", value); }
    public decimal MaxBuysPerStage { get => S.Upgrade.MaxBuysPerStage; set => Up(u => u with { MaxBuysPerStage = (int)value }); }

    public void Move(PriorityItemViewModel item, int delta)
    {
        int i = Priority.IndexOf(item), j = i + delta;
        if (i < 0 || j < 0 || j >= Priority.Count) return;
        Priority.Move(i, j);
        Up(u => u with { Priority = Priority.Select(p => p.Name).ToList() });
    }
}

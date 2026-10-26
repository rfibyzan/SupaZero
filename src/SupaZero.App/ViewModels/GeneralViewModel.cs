using SupaZero.Core.Game;
using SupaZero.Core.Input;

namespace SupaZero.App.ViewModels;

public sealed class GeneralViewModel(MainViewModel root) : ObservableObject
{
    private bool _isRebinding;
    private string _hotkeyStatus = string.Empty;

    public MainViewModel Root => root;
    public string HotkeyText => _isRebinding ? "..." : root.Settings.General.MacroHotkey;
    public string RebindText => _isRebinding ? "Press a key" : "Rebind";
    public bool IsRebinding => _isRebinding;
    public string HotkeyStatus { get => _hotkeyStatus; private set => Set(ref _hotkeyStatus, value); }
    public string HotkeyHint => $"Press {root.Settings.General.MacroHotkey} to Start/Stop the macro.";

    public Func<string?, bool>? RegisterHotkey { get; set; }
    public Func<string?>? HotkeyError { get; set; }

    public RelayCommand RebindCommand => new(_ =>
    {
        _isRebinding = true;
        root.ActiveRebind = CompleteRebind;
        RaiseHotkey();
    });
    public RelayCommand ToggleCommand => new(_ => root.Runner.Toggle());
    public System.Collections.ObjectModel.ObservableCollection<SupaZero.Core.Game.CalibrationItem> CalibrationItems { get; } = [];
    private string _calibrationSummary = "Not checked yet.";
    public bool HasCalibration => CalibrationItems.Count > 0;
    public string CalibrationSummary { get => _calibrationSummary; private set => Set(ref _calibrationSummary, value); }

    /// <summary>Quick calibration: one memory read; summary + collapsible details on the card, a single log line.</summary>
    public RelayCommand CalibrateCommand => new(_ =>
    {
        var snap = root.Runner.IsRunning ? root.Runner.LastSnapshot ?? root.ReadSnapshot() : root.ReadSnapshot();
        if (snap.Equipped.Count == 0 && root.Powers.SavedLoadout() is { Count: > 0 } saved) snap = snap with { Equipped = saved };
        var items = SupaZero.Core.Game.Calibration.Run(snap, root.Settings);
        CalibrationItems.Clear();
        foreach (var i in items) CalibrationItems.Add(i);
        CalibrationSummary = $"{DateTime.Now:HH:mm:ss} · {SupaZero.Core.Game.Calibration.Summary(items)}";
        Raise(nameof(HasCalibration));
        root.AddLog("Calibration: " + SupaZero.Core.Game.Calibration.Summary(items) + " (details on the Calibration card).");
    });

    /// <summary>v0.2.82: action calibration — the character opens Trait/Spin and the room menu to read powers, maps and acts, then Leaves.</summary>
    public RelayCommand CalibrateActionCommand => new(_ =>
    {
        if (root.Runner.IsRunning) { root.AddLog("Action calibration: stop the macro first."); return; }
        root.Runner.Start(calibrate: true);
    });

    public RelayCommand ClearLogCommand => new(_ => root.LogLines.Clear());

    public void ApplyHotkey()
    {
        bool ok = RegisterHotkey?.Invoke(root.Settings.General.MacroHotkey) ?? false;
        HotkeyStatus = ok ? string.Empty : HotkeyError?.Invoke() ?? string.Empty;
    }

    /// <summary>Called via MainViewModel.ActiveRebind when the next key is pressed. True = event handled.</summary>
    public bool CompleteRebind(string? name, bool cancel)
    {
        if (!_isRebinding) return false;
        _isRebinding = false;
        if (!cancel && HotkeyMap.Normalize(name) is { } key)
        {
            root.Settings.General = root.Settings.General with { MacroHotkey = key };
            root.Save();
            ApplyHotkey();
        }
        RaiseHotkey();
        return true;
    }

    private void RaiseHotkey()
    {
        Raise(nameof(HotkeyText));
        Raise(nameof(RebindText));
        Raise(nameof(IsRebinding));
        Raise(nameof(HotkeyHint));
    }

    // Round status card
    public string ScreenText => root.Snapshot.Screen.ToString();
    public string PhaseText => root.StatusText;
    public string AreaText => root.Snapshot.Hud is { AreaTotal: > 0 } h ? $"{h.Area}/{h.AreaTotal}" : "-";
    public string EnemiesText => root.Snapshot.EnemyCount >= 0 ? root.Snapshot.EnemyCount.ToString() : "-";
    public string StagesText => root.Runner.Machine.StagesCleared.ToString();
    public string RetriesText => root.Runner.Machine.Retries.ToString();
    public string HudRawText => root.Snapshot.HudRaw ?? "-";

    // Equipped powers card
    public string Power1Name => PowerName(1);
    public string Power1Detail => PowerDetail(1);
    public string Power2Name => PowerName(2);
    public string Power2Detail => PowerDetail(2);

    private EquippedPower? Power(int loadout) => root.Snapshot.Equipped.FirstOrDefault(p => p.Loadout == loadout);
    private string PowerName(int loadout) => Power(loadout)?.Name ?? "-";

    private string PowerDetail(int loadout)
    {
        if (Power(loadout) is not { } p) return root.Snapshot.Attached ? "Not read yet (open the character menu once in the lobby)" : "-";
        var parts = new List<string>();
        if (p.Level is { } lv) parts.Add($"Lv.{lv}");
        if (p.Rarity is { Length: > 0 } r) parts.Add(r);
        parts.Add(p.Trait is { } t
            ? $"Trait {t.Name}{(t.Chance is { } c ? $" ({c})" : "")}{(t.Buffs.Count > 0 ? " · " + string.Join(" ", t.Buffs) : "")}"
            : "Trait ? (open the Trait menu once)");
        return string.Join(" · ", parts);
    }

    public void Refresh()
    {
        Raise(nameof(Power1Name));
        Raise(nameof(Power1Detail));
        Raise(nameof(Power2Name));
        Raise(nameof(Power2Detail));
        Raise(nameof(ScreenText));
        Raise(nameof(PhaseText));
        Raise(nameof(AreaText));
        Raise(nameof(EnemiesText));
        Raise(nameof(StagesText));
        Raise(nameof(RetriesText));
        Raise(nameof(HudRawText));
    }
}

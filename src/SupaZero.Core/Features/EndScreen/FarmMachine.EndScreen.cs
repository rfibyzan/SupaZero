using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    private List<MacroAction> Victory(GameSnapshot s, AppSettings S, long now)
    {
        var after = S.Farm.AfterWin == AfterWin.Auto
            ? (Matches(S.Farm.Mode, nameof(FarmMode.Story)) || Matches(S.Farm.Mode, nameof(FarmMode.Trials)) ? AfterWin.Next : AfterWin.Retry)
            : S.Farm.AfterWin;
        GuiRect? btn; string label;
        (btn, label) = after switch
        {
            AfterWin.Next => (s.NextButton, "Next stage"),
            AfterWin.Retry => (s.RetryButton, "Retry"),
            _ => (s.LeaveButton, "Leave"),
        };
        if (S.Farm.Act == 0) (after, btn, label) = s.NextButton is { } nb ? (AfterWin.Next, (GuiRect?)nb, "Next act") : (AfterWin.Leave, s.LeaveButton, "Leave");
        if (after == AfterWin.Next && btn is null && s.RetryButton is null && s.LeaveButton is not null)
            return Stop("Victory, no Next button → Story finished.");
        if (btn is not { } b)
        {
            Phase = MacroPhase.Idle;
            Status = $"Victory, {label} button not readable.";
            return Note("no-btn", now, 5000, $"{label} button not readable on EndScreen.");
        }
        Phase = MacroPhase.Advancing;
        Status = Matches(S.Farm.Mode, nameof(FarmMode.Infinite)) ? $"Infinite finished ({s.ResultText}) → {label}" : $"Victory (#{StagesCleared}) → {label}";
        if (!Ready("after-win", now, 2500)) return [];
        _afterWinClicks++;
        if (_actAdvancePending) { if (after == AfterWin.Next) _selectedAct++; _actAdvancePending = false; }
        var click = new ClickAction(b.CenterX, b.CenterY, label) { InRound = true };
        return _afterWinClicks % 4 == 0
            ? [click, new NoteAction($"Still on EndScreen after {_afterWinClicks}x clicking {label} at ({b.CenterX:0},{b.CenterY:0}).")]
            : [click];
    }

    internal const long UnitSlotTimeoutMs = 15000, UnitsOpenTimeoutMs = 8000, SkillTreeCloseTimeoutMs = 6000, BuyConfirmMs = 3000;
    private long? _upOpenAt, _upCloseAt;
    private bool _upClosing;
    private string? _upSlotKey;
    private int _upSlotTries, _upBuyFails;

    private long? _unitPickStart;
    private void UpgradeFinish() { _unitPickStart = null; _upgradeDoneThisStage = true; _upClosing = false; _upOpenAt = _upCloseAt = null; _upSlotKey = null; _upSlotTries = _upBuyFails = 0; _pendingBuyConfirm = false; }

    private List<MacroAction> Defeat(GameSnapshot s, AppSettings S, long now)
    {
        var f = S.Farm;
        bool unlimited = f.InfiniteUnlimitedRetry && Matches(f.Mode, nameof(FarmMode.Infinite));
        if (f.RetryOnDefeat && (unlimited || _retries < f.MaxRetriesPerStage) && s.RetryButton is { } retry)
        {
            Phase = MacroPhase.Retrying;
            Status = unlimited ? $"Infinite finished → Retry (#{_retries + 1}, unlimited)" : $"DEFEAT → Retry ({_retries + 1}/{f.MaxRetriesPerStage})";
            if (!Ready("retry", now, 3000)) return [];
            _retries++;
            return [new ClickAction(retry.CenterX, retry.CenterY, "Retry") { InRound = true }];
        }
        return Stop("DEFEAT, retries exhausted → macro stopped.");
    }

    // ---------- util ----------
}

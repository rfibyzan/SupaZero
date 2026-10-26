using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>
    /// v0.2.92: SkillTree upgrade in the lobby before heading to a room, for the CURRENTLY selected unit (unit bar is not touched).
    /// LobbyHUD Units → SkillTree opens → click "!" node by priority → node selected (Bolder) + BuyFrame shown → BuyButton →
    /// success when tokens drop → repeat until the limit / no nodes left → X button. Only nodes, BuyButton, X and Units are clicked.
    /// null = done / skipped, continue walking to the room.
    /// </summary>
    private List<MacroAction>? LobbyUpgrade(GameSnapshot s, UpgradeSettings up, long now)
    {
        Phase = MacroPhase.Upgrading;
        var stand = new MoveAction([], "Upgrade");
        if (!s.SkillTreeOpen)
        {
            if (_upClosing) { UpgradeFinish(); return up.DryRun ? Stop("Dry run finished (nothing bought) → macro stopped.") : null; }
            _upOpenAt ??= now;
            if (now - _upOpenAt.Value > UnitsOpenTimeoutMs)
            {
                UpgradeFinish();
                if (up.DryRun) return Stop($"Dry run: SkillTree did not open within {UnitsOpenTimeoutMs / 1000} s → macro stopped.");
                return [stand, .. Note("up-noopen", now, 1000, $"SkillTree did not open within {UnitsOpenTimeoutMs / 1000} s → upgrade skipped.")];
            }
            if (s.UnitsButton is not { } u)
            {
                if (now - _upOpenAt.Value < 1500) return [stand];
                UpgradeFinish();
                if (up.DryRun) return Stop("Dry run: Units button (LobbyHUD) not read → macro stopped.");
                return [stand, .. Note("up-nounits", now, 1000, "Units button (LobbyHUD) not read → upgrade skipped.")];
            }
            if (!InWindow(s, u.CenterX, u.CenterY)) { UpgradeFinish(); if (up.DryRun) return Stop("Dry run: Units button outside the Roblox window → macro stopped."); return [stand, .. Note("up-unitsout", now, 1000, "Units button outside the Roblox window → upgrade skipped.")]; }
            Status = "Upgrade: opening Units.";
            return Ready("up-units", now, 3000) ? [stand, new ClickAction(u.CenterX, u.CenterY, "Units button (LobbyHUD)")] : [stand];
        }

        if (_upClosing)
        {
            _upCloseAt ??= now;
            if (now - _upCloseAt.Value > SkillTreeCloseTimeoutMs)
            {
                UpgradeFinish();
                return Stop($"SkillTree did not close within {SkillTreeCloseTimeoutMs / 1000} s → macro stopped (close it manually).");
            }
            Status = "Upgrade: closing SkillTree.";
            if (s.SkillTreeExit is { } x && InWindow(s, x.CenterX, x.CenterY))
                return Ready("up-exit", now, 1500) ? [stand, new ClickAction(x.CenterX, x.CenterY, "Close SkillTree (X)")] : [stand];
            return [stand, .. Note("up-noexit", now, 5000, "SkillTree X button not read.")];
        }

        List<MacroAction> Close(string why)
        {
            _upClosing = true; _upCloseAt = null;
            return [stand, .. Note("up-done", now, 1000, $"Upgrade {s.SkillUnitName ?? (s.SkillUnitSlot is int n ? $"slot {n}" : "unit ?")}: {why} (bought {_boughtThisStage}, tokens {s.Tokens?.ToString() ?? "?"}).")];
        }

        string unit = s.SkillUnitName is { } un ? $"{un}{(s.SkillUnitSlot is int us ? $" (slot {us})" : "")}" : s.SkillUnitSlot is int us2 ? $"slot {us2}" : "unit ?";
        _pending.AddRange(Note("up-unit:" + unit, now, 600000, $"Upgrade SkillTree: {unit}, tokens {s.Tokens?.ToString() ?? "?"}."));
        if (_pendingBuyConfirm)
        {
            if (s.Tokens is int t && _tokensBeforeBuy is int b && t < b)
            {
                _boughtThisStage++; _pendingBuyConfirm = false; _upSlotKey = null; _upSlotTries = 0; _upBuyFails = 0;
                _pending.AddRange(Note("up-bought", now, 0, $"Upgrade bought: tokens {b} → {t}."));
            }
            else if (now - _buyAt < BuyConfirmMs) return [stand];
            else { _pendingBuyConfirm = false; if (++_upBuyFails >= 2) return Close("Buy clicked but tokens did not drop 2×, stopping"); }
        }
        // v0.3.3 (Byzan 25 Oct 08:38): target unit = slot number in the SkillTree unit list (same order as Trait/Power Spin), not the name.
        if (up.TargetSlot > 0 && !_pendingBuyConfirm && s.SkillUnitSlot != up.TargetSlot)
        {
            int ts = up.TargetSlot;
            _unitPickStart ??= now;
            if (now - _unitPickStart.Value > UnitSlotTimeoutMs) return Close($"Slot {ts} could not be selected within {UnitSlotTimeoutMs / 1000} s → not buying");
            if (s.SkillUnits.FirstOrDefault(u => u.Name == $"Slot{ts}") is not { } te)
                return s.SkillUnits.Count == 0 && now - _unitPickStart.Value < 2000 ? [stand] : Close($"Slot {ts} not in the SkillTree unit list → not buying");
            if (te.Locked) return Close($"Slot {ts} empty (no unit) → not buying");
            Status = $"Upgrade: selecting unit Slot {ts} (currently slot {s.SkillUnitSlot?.ToString() ?? "?"}).";
            return [stand, .. ClickEntry(s, te, s.SkillUnitView, $"up-unit{ts}", $"Unit Slot{ts}", now) ?? []];
        }
        _unitPickStart = null;
        if (s.Tokens is not int tok) return Close("tokens not read, not buying");
        if (_boughtThisStage >= up.MaxBuysPerStage) return Close("buy limit reached");
        var cols = up.ActiveColumns;
        if (cols.Count == 0) return Close("no columns switched ON");
        var slot = UpgradePlanner.Next(s.Slots, cols, up.Mode, _boughtThisStage, tok);
        if (slot is null) return Close("no buyable nodes");
        string key = $"{slot.Column}#{slot.Index}";
        if (key != _upSlotKey) { _upSlotKey = key; _upSlotTries = 0; }

        // Planned node is already selected and the Buy button is shown → buy.
        if (slot.Selected && s.BuyButton is { } buy)
        {
            if (!InWindow(s, buy.CenterX, buy.CenterY)) return Close("Buy button outside the window");
            if (up.DryRun)
                return Close($"DRY RUN: would buy {slot.Column} #{slot.Index + 1} ({slot.Price} tokens), Buy button read at ({buy.CenterX},{buy.CenterY}) but NOT clicked");
            if (!Ready("up-buy", now, 800)) return [stand];
            _pendingBuyConfirm = true; _tokensBeforeBuy = tok; _buyAt = now;
            Status = $"Upgrade {s.SkillUnitName ?? "unit"}: buying {slot.Column} #{slot.Index + 1} ({slot.Price} tokens, {tok} left)";
            return [stand, new ClickAction(buy.CenterX, buy.CenterY, $"Buy {slot.Column} #{slot.Index + 1}")];
        }
        if (_upSlotTries >= 3) return Close($"node {slot.Column} #{slot.Index + 1} not selected after 3× clicks");
        var r = slot.Rect!.Value;
        if (!InWindow(s, r.CenterX, r.CenterY)) return Close("node outside the window");
        Status = $"Upgrade: selecting {slot.Column} #{slot.Index + 1} ({slot.Price} tokens)";
        if (!Ready("up-slot", now, 1000)) return [stand];
        _upSlotTries++;
        return [stand, new ClickAction(r.CenterX, r.CenterY, $"Node {slot.Column} #{slot.Index + 1}")];
    }
    private long _buyAt;
}

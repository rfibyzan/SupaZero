using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>
    /// v0.2.92: upgrade SkillTree di lobby sebelum ke room, untuk unit yang SEDANG terpilih (bar unit tidak disentuh).
    /// LobbyHUD Units → SkillTree terbuka → klik node "!" sesuai prioritas → node terpilih (Bolder) + BuyFrame tampil → BuyButton →
    /// berhasil bila token turun → ulang sampai batas / tidak ada node → tombol X. Hanya node, BuyButton, X, dan Units yang diklik.
    /// null = selesai / dilewati, lanjut jalan ke room.
    /// </summary>
    private List<MacroAction>? LobbyUpgrade(GameSnapshot s, UpgradeSettings up, long now)
    {
        Phase = MacroPhase.Upgrading;
        var stand = new MoveAction([], "Upgrade");
        if (!s.SkillTreeOpen)
        {
            if (_upClosing) { UpgradeFinish(); return up.DryRun ? Stop("Mode uji selesai (tidak ada yang dibeli) → macro berhenti.") : null; }
            _upOpenAt ??= now;
            if (now - _upOpenAt.Value > UnitsOpenTimeoutMs)
            {
                UpgradeFinish();
                if (up.DryRun) return Stop($"Mode uji: SkillTree tidak terbuka dalam {UnitsOpenTimeoutMs / 1000} dtk → macro berhenti.");
                return [stand, .. Note("up-noopen", now, 1000, $"SkillTree tidak terbuka dalam {UnitsOpenTimeoutMs / 1000} dtk → upgrade dilewati.")];
            }
            if (s.UnitsButton is not { } u)
            {
                if (now - _upOpenAt.Value < 1500) return [stand];
                UpgradeFinish();
                if (up.DryRun) return Stop("Mode uji: tombol Units (LobbyHUD) tidak terbaca → macro berhenti.");
                return [stand, .. Note("up-nounits", now, 1000, "Tombol Units (LobbyHUD) tidak terbaca → upgrade dilewati.")];
            }
            if (!InWindow(s, u.CenterX, u.CenterY)) { UpgradeFinish(); if (up.DryRun) return Stop("Mode uji: tombol Units di luar jendela Roblox → macro berhenti."); return [stand, .. Note("up-unitsout", now, 1000, "Tombol Units di luar jendela Roblox → upgrade dilewati.")]; }
            Status = "Upgrade: buka Units.";
            return Ready("up-units", now, 3000) ? [stand, new ClickAction(u.CenterX, u.CenterY, "Tombol Units (LobbyHUD)")] : [stand];
        }

        if (_upClosing)
        {
            _upCloseAt ??= now;
            if (now - _upCloseAt.Value > SkillTreeCloseTimeoutMs)
            {
                UpgradeFinish();
                return Stop($"SkillTree tidak tertutup dalam {SkillTreeCloseTimeoutMs / 1000} dtk → macro berhenti (tutup manual).");
            }
            Status = "Upgrade: tutup SkillTree.";
            if (s.SkillTreeExit is { } x && InWindow(s, x.CenterX, x.CenterY))
                return Ready("up-exit", now, 1500) ? [stand, new ClickAction(x.CenterX, x.CenterY, "Tutup SkillTree (X)")] : [stand];
            return [stand, .. Note("up-noexit", now, 5000, "Tombol X SkillTree tidak terbaca.")];
        }

        List<MacroAction> Close(string why)
        {
            _upClosing = true; _upCloseAt = null;
            return [stand, .. Note("up-done", now, 1000, $"Upgrade {s.SkillUnitName ?? (s.SkillUnitSlot is int n ? $"slot {n}" : "unit ?")}: {why} (beli {_boughtThisStage}, token {s.Tokens?.ToString() ?? "?"}).")];
        }

        string unit = s.SkillUnitName is { } un ? $"{un}{(s.SkillUnitSlot is int us ? $" (slot {us})" : "")}" : s.SkillUnitSlot is int us2 ? $"slot {us2}" : "unit ?";
        _pending.AddRange(Note("up-unit:" + unit, now, 600000, $"Upgrade SkillTree: {unit}, token {s.Tokens?.ToString() ?? "?"}."));
        if (_pendingBuyConfirm)
        {
            if (s.Tokens is int t && _tokensBeforeBuy is int b && t < b)
            {
                _boughtThisStage++; _pendingBuyConfirm = false; _upSlotKey = null; _upSlotTries = 0; _upBuyFails = 0;
                _pending.AddRange(Note("up-bought", now, 0, $"Upgrade terbeli: token {b} → {t}."));
            }
            else if (now - _buyAt < BuyConfirmMs) return [stand];
            else { _pendingBuyConfirm = false; if (++_upBuyFails >= 2) return Close("Buy diklik tapi token tidak turun 2×, berhenti"); }
        }
        // v0.3.3 (Byzan 25 Okt 08:38): unit target = nomor slot di daftar unit SkillTree (urutan sama dengan Trait/Power Spin), bukan nama.
        if (up.TargetSlot > 0 && !_pendingBuyConfirm && s.SkillUnitSlot != up.TargetSlot)
        {
            int ts = up.TargetSlot;
            _unitPickStart ??= now;
            if (now - _unitPickStart.Value > UnitSlotTimeoutMs) return Close($"Slot {ts} tidak berhasil dipilih dalam {UnitSlotTimeoutMs / 1000} dtk → tidak membeli");
            if (s.SkillUnits.FirstOrDefault(u => u.Name == $"Slot{ts}") is not { } te)
                return s.SkillUnits.Count == 0 && now - _unitPickStart.Value < 2000 ? [stand] : Close($"Slot {ts} tidak ada di daftar unit SkillTree → tidak membeli");
            if (te.Locked) return Close($"Slot {ts} kosong (tidak ada unit) → tidak membeli");
            Status = $"Upgrade: pilih unit Slot {ts} (sekarang slot {s.SkillUnitSlot?.ToString() ?? "?"}).";
            return [stand, .. ClickEntry(s, te, s.SkillUnitView, $"up-unit{ts}", $"Unit Slot{ts}", now) ?? []];
        }
        _unitPickStart = null;
        if (s.Tokens is not int tok) return Close("token tidak terbaca, tidak membeli");
        if (_boughtThisStage >= up.MaxBuysPerStage) return Close("batas beli tercapai");
        var cols = up.ActiveColumns;
        if (cols.Count == 0) return Close("tidak ada kolom yang di-ON-kan");
        var slot = UpgradePlanner.Next(s.Slots, cols, up.Mode, _boughtThisStage, tok);
        if (slot is null) return Close("tidak ada node yang bisa dibeli");
        string key = $"{slot.Column}#{slot.Index}";
        if (key != _upSlotKey) { _upSlotKey = key; _upSlotTries = 0; }

        // Node rencana sudah terpilih dan tombol Buy tampil → beli.
        if (slot.Selected && s.BuyButton is { } buy)
        {
            if (!InWindow(s, buy.CenterX, buy.CenterY)) return Close("tombol Buy di luar jendela");
            if (up.DryRun)
                return Close($"MODE UJI: akan beli {slot.Column} #{slot.Index + 1} ({slot.Price} token), tombol Buy terbaca di ({buy.CenterX},{buy.CenterY}) tapi TIDAK diklik");
            if (!Ready("up-buy", now, 800)) return [stand];
            _pendingBuyConfirm = true; _tokensBeforeBuy = tok; _buyAt = now;
            Status = $"Upgrade {s.SkillUnitName ?? "unit"}: beli {slot.Column} #{slot.Index + 1} ({slot.Price} token, sisa {tok})";
            return [stand, new ClickAction(buy.CenterX, buy.CenterY, $"Buy {slot.Column} #{slot.Index + 1}")];
        }
        if (_upSlotTries >= 3) return Close($"node {slot.Column} #{slot.Index + 1} tidak terpilih setelah 3× klik");
        var r = slot.Rect!.Value;
        if (!InWindow(s, r.CenterX, r.CenterY)) return Close("node di luar jendela");
        Status = $"Upgrade: pilih {slot.Column} #{slot.Index + 1} ({slot.Price} token)";
        if (!Ready("up-slot", now, 1000)) return [stand];
        _upSlotTries++;
        return [stand, new ClickAction(r.CenterX, r.CenterY, $"Node {slot.Column} #{slot.Index + 1}")];
    }
    private long _buyAt;
}

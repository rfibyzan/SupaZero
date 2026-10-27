using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>v0.3.24 (Byzan 27 Oct): carry alt is down and PlayerGui.FreeRevive shows "Revive · Free" (key E, once per run).
    /// Press E only when own scoreboard damage is readable and below the carry target; target met or unreadable = stay down.</summary>
    private List<MacroAction> FreeRevive(GameSnapshot s, AppSettings S, long now)
    {
        int target = S.Combat.CarryTarget;
        if (s.MyDamage is not { } dmg)
        {
            Status = "Down: own damage unreadable, free revive not used.";
            return Note("revive-unknown", now, 10000, "Free revive shown but own damage (RunPlayerList row = UserId) is unreadable → not pressing E.");
        }
        if (dmg >= target)
        {
            Status = $"Down: damage {dmg:N0} ≥ {target:N0}, free revive not needed.";
            return Note("revive-skip", now, 10000, $"Free revive skipped: carry damage {dmg:N0} already ≥ {target:N0}.");
        }
        Status = $"Down: damage {dmg:N0} < {target:N0} → free revive (E).";
        if (!Ready("free-revive", now, 2000)) return [];
        return [new KeyAction(VirtualKeys.E, 80, $"Free revive (E), damage {dmg:N0} < {target:N0}")];
    }
}

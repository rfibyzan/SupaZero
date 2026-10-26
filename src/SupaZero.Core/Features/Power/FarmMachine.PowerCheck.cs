using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    // ---------- Power + trait check (once per Start) ----------
    private int _pcStep;          // 0 = Trait, 1 = Spin, 2 = done
    private long? _pcStepAt, _pcOpenAt;
    private int _pcCloseTries;
    public const int PowerMenuReadMs = 1500, PowerMenuTimeoutMs = 15000; // v0.3.13: 3000→1500 (Roll slots are read every 1 s)

    /// <summary>
    /// Lobby, before Create Room: click the Trait button in LobbyHUD → wait until the Trait screen is read (3 s) → click again (close) →
    /// Spin button → character slot list read → close. Only LobbyHUD buttons are clicked, never buttons inside the menu
    /// (roll/reroll spend Spins/Rerolls). Button not readable / menu does not open → step skipped.
    /// </summary>
    private List<MacroAction>? PowerCheck(GameSnapshot s, long now)
    {
        if (_pcStep >= 2) return null;
        Phase = MacroPhase.Lobby;
        string key = _pcStep == 0 ? AnimeZeroPaths.MenuTrait : AnimeZeroPaths.MenuSpin;
        GuiRect? btn = (_pcStep == 1 ? s.SpinButton : null) ?? s.HubMenuButtons.FirstOrDefault(b => b.Name == key)?.Rect;
        // v0.3.12: Roll open → SummonButton shifts off screen (s0819: AbsolutePosition X -262) and ExitButton shows.
        bool open = _pcStep == 0 ? s.TraitMenuOpen
            : s.SpinMenuOpen && !(s.SpinButton is { } sb && InWindow(s, sb.CenterX, sb.CenterY));
        _pcStepAt ??= now;
        var stand = new MoveAction([], "Power check");

        void Next() { _pcStep++; _pcStepAt = null; _pcOpenAt = null; }

        if (open)
        {
            _pcOpenAt ??= now;
            Status = $"Power check: {key} menu open, reading…";
            if (now - _pcOpenAt.Value < PowerMenuReadMs) return [stand];
            var acts = new List<MacroAction> { stand };
            // v0.3.12: Roll covers the LobbyHUD button (SummonButton shifts off screen) → close via ExitButton.
            var close = _pcStep == 1 ? s.RollExit ?? btn : btn;
            if (_pcStep == 1)
            {
                // v0.3.12b: Roll only counts as done once it has actually closed; Exit is re-clicked every 1.5 s (max 4×).
                if (_pcCloseTries >= 4)
                {
                    _pcCloseTries = 0; Next();
                    return [stand, .. Note("pc-close-fail", now, 1000, "Spinning menu did not close after 4× clicking Exit → continuing (check the Exit button position).")];
                }
                if (_pcCloseTries == 0 && Calibrating) _calNotes.Add(s.Equipped.Count == 0 ? "Spinning opened, equipped powers not read yet."
                    : "Power: " + string.Join(" · ", s.Equipped.OrderBy(p => p.Loadout).Select(p => $"slot {p.Loadout} {p.Name}")));
                if (close is { } c1 && InWindow(s, c1.CenterX, c1.CenterY) && Ready("pc-exit", now, 1500))
                {
                    _pcCloseTries++;
                    Status = "Power check: closing Spinning (Exit).";
                    acts.Add(new ClickAction(c1.CenterX, c1.CenterY, $"Exit Spinning ({_pcCloseTries})"));
                }
                else if (close is null) acts.AddRange(Note("pc-noexit", now, 5000, "Spinning is open but the Exit button is not readable."));
                if (_pcCloseTries == 1 && acts.Count > 1 && acts[^1] is ClickAction)
                {
                    string eq1 = s.Equipped.Count == 0 ? "not read yet" : string.Join(" · ", s.Equipped.OrderBy(p => p.Loadout).Select(p =>
                        $"slot {p.Loadout} {p.Name}{(p.Level is { } lv1 ? $" Lv.{lv1}" : "")}{(p.Trait is { } t1 ? $" ({t1.Name})" : "")}"));
                    acts.AddRange(Note("pc-" + key, now, 1000, $"Power check ({key}): {eq1}."));
                }
                return acts;
            }
            if (close is { } b && InWindow(s, b.CenterX, b.CenterY)) acts.Add(new ClickAction(b.CenterX, b.CenterY, $"Close {key} menu"));
            if (s.Equipped.Count > 0)
            {
                string eq = s.Equipped.Count == 0 ? "not read yet" : string.Join(" · ", s.Equipped.OrderBy(p => p.Loadout).Select(p =>
                    $"slot {p.Loadout} {p.Name}{(p.Level is { } lv ? $" Lv.{lv}" : "")}{(p.Trait is { } t ? $" ({t.Name})" : "")}"));
                acts.AddRange(Note("pc-" + key, now, 1000, $"Power check ({key}): {eq}."));
            }
            Next();
            return acts;
        }
        if (_pcStep == 1 && _pcCloseTries > 0) { _pcCloseTries = 0; Next(); return PowerCheck(s, now); } // Roll closed after Exit
        if (now - _pcStepAt.Value > PowerMenuTimeoutMs)
        {
            Next();
            return [stand, .. Note("pc-skip-" + key, now, 1000, $"{key} menu did not open within {PowerMenuTimeoutMs / 1000} s → skipped.")];
        }
        if (btn is { } r)
        {
            Status = $"Power check: opening {key} menu.";
            if (!InWindow(s, r.CenterX, r.CenterY)) { Next(); return [stand, .. Note("pc-out-" + key, now, 1000, $"{key} button is outside the Roblox window → skipped.")]; }
            return Ready("pc-" + key, now, 3000) ? [stand, new ClickAction(r.CenterX, r.CenterY, $"{key} button (LobbyHUD)")] : [stand];
        }
        // Button not readable → skip right away (do not hold up walking to the room), continue with the next step in the same tick.
        if (s.HubMenuButtons.Count > 0 || s.AreasButton is not null)
            _pending.AddRange(Note("pc-nobtn-" + key, now, 60000, $"{key} button in LobbyHUD not readable → {key} check skipped (using saved powers)."));
        Next();
        return PowerCheck(s, now);
    }
}

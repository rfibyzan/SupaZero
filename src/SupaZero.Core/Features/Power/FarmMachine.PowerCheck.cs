using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    // ---------- Cek power + trait (sekali tiap Start) ----------
    private int _pcStep;          // 0 = Trait, 1 = Spin, 2 = selesai
    private long? _pcStepAt, _pcOpenAt;
    private int _pcCloseTries;
    public const int PowerMenuReadMs = 1500, PowerMenuTimeoutMs = 15000; // v0.3.13: 3000→1500 (slot Roll dibaca tiap 1 dtk)

    /// <summary>
    /// Lobby, sebelum Create Room: klik tombol Trait di LobbyHUD → tunggu layar Trait terbaca (3 dtk) → klik lagi (tutup) →
    /// tombol Spin → daftar slot karakter terbaca → tutup. Hanya tombol LobbyHUD yang diklik, tidak ada tombol di dalam menu
    /// (roll/reroll memakai Spins/Rerolls). Tombol tidak terbaca / menu tidak terbuka → langkah dilewati.
    /// </summary>
    private List<MacroAction>? PowerCheck(GameSnapshot s, long now)
    {
        if (_pcStep >= 2) return null;
        Phase = MacroPhase.Lobby;
        string key = _pcStep == 0 ? AnimeZeroPaths.MenuTrait : AnimeZeroPaths.MenuSpin;
        GuiRect? btn = (_pcStep == 1 ? s.SpinButton : null) ?? s.HubMenuButtons.FirstOrDefault(b => b.Name == key)?.Rect;
        // v0.3.12: Roll terbuka → SummonButton bergeser keluar layar (s0819: AbsolutePosition X -262) dan ExitButton tampil.
        bool open = _pcStep == 0 ? s.TraitMenuOpen
            : s.SpinMenuOpen && !(s.SpinButton is { } sb && InWindow(s, sb.CenterX, sb.CenterY));
        _pcStepAt ??= now;
        var stand = new MoveAction([], "Cek power");

        void Next() { _pcStep++; _pcStepAt = null; _pcOpenAt = null; }

        if (open)
        {
            _pcOpenAt ??= now;
            Status = $"Cek power: menu {key} terbuka, membaca…";
            if (now - _pcOpenAt.Value < PowerMenuReadMs) return [stand];
            var acts = new List<MacroAction> { stand };
            // v0.3.12: Roll menutupi tombol LobbyHUD (SummonButton bergeser keluar layar) → tutup lewat ExitButton.
            var close = _pcStep == 1 ? s.RollExit ?? btn : btn;
            if (_pcStep == 1)
            {
                // v0.3.12b: Roll baru dianggap selesai setelah benar-benar tertutup; Exit diklik ulang tiap 1,5 dtk (maks 4×).
                if (_pcCloseTries >= 4)
                {
                    _pcCloseTries = 0; Next();
                    return [stand, .. Note("pc-close-fail", now, 1000, "Menu Spinning tidak tertutup setelah 4× klik Exit → lanjut (cek posisi tombol Exit).")];
                }
                if (_pcCloseTries == 0 && Calibrating) _calNotes.Add(s.Equipped.Count == 0 ? "Spinning dibuka, power terpasang belum terbaca."
                    : "Power: " + string.Join(" · ", s.Equipped.OrderBy(p => p.Loadout).Select(p => $"slot {p.Loadout} {p.Name}")));
                if (close is { } c1 && InWindow(s, c1.CenterX, c1.CenterY) && Ready("pc-exit", now, 1500))
                {
                    _pcCloseTries++;
                    Status = "Cek power: tutup Spinning (Exit).";
                    acts.Add(new ClickAction(c1.CenterX, c1.CenterY, $"Exit Spinning ({_pcCloseTries})"));
                }
                else if (close is null) acts.AddRange(Note("pc-noexit", now, 5000, "Spinning terbuka tapi tombol Exit tidak terbaca."));
                if (_pcCloseTries == 1 && acts.Count > 1 && acts[^1] is ClickAction)
                {
                    string eq1 = s.Equipped.Count == 0 ? "belum terbaca" : string.Join(" · ", s.Equipped.OrderBy(p => p.Loadout).Select(p =>
                        $"slot {p.Loadout} {p.Name}{(p.Level is { } lv1 ? $" Lv.{lv1}" : "")}{(p.Trait is { } t1 ? $" ({t1.Name})" : "")}"));
                    acts.AddRange(Note("pc-" + key, now, 1000, $"Cek power ({key}): {eq1}."));
                }
                return acts;
            }
            if (close is { } b && InWindow(s, b.CenterX, b.CenterY)) acts.Add(new ClickAction(b.CenterX, b.CenterY, $"Tutup menu {key}"));
            if (s.Equipped.Count > 0)
            {
                string eq = s.Equipped.Count == 0 ? "belum terbaca" : string.Join(" · ", s.Equipped.OrderBy(p => p.Loadout).Select(p =>
                    $"slot {p.Loadout} {p.Name}{(p.Level is { } lv ? $" Lv.{lv}" : "")}{(p.Trait is { } t ? $" ({t.Name})" : "")}"));
                acts.AddRange(Note("pc-" + key, now, 1000, $"Cek power ({key}): {eq}."));
            }
            Next();
            return acts;
        }
        if (_pcStep == 1 && _pcCloseTries > 0) { _pcCloseTries = 0; Next(); return PowerCheck(s, now); } // Roll tertutup setelah Exit
        if (now - _pcStepAt.Value > PowerMenuTimeoutMs)
        {
            Next();
            return [stand, .. Note("pc-skip-" + key, now, 1000, $"Menu {key} tidak terbuka dalam {PowerMenuTimeoutMs / 1000} dtk → dilewati.")];
        }
        if (btn is { } r)
        {
            Status = $"Cek power: buka menu {key}.";
            if (!InWindow(s, r.CenterX, r.CenterY)) { Next(); return [stand, .. Note("pc-out-" + key, now, 1000, $"Tombol {key} di luar jendela Roblox → dilewati.")]; }
            return Ready("pc-" + key, now, 3000) ? [stand, new ClickAction(r.CenterX, r.CenterY, $"Tombol {key} (LobbyHUD)")] : [stand];
        }
        // Tombol tidak terbaca → langsung lewati (tidak menahan jalan ke room), lanjut langkah berikutnya di tick yang sama.
        if (s.HubMenuButtons.Count > 0 || s.AreasButton is not null)
            _pending.AddRange(Note("pc-nobtn-" + key, now, 60000, $"Tombol {key} di LobbyHUD tidak terbaca → cek {key} dilewati (pakai power tersimpan)."));
        Next();
        return PowerCheck(s, now);
    }
}

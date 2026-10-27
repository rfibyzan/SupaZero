using SupaZero.Core.Features.Codes;
using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>v0.3.26: codes still to try for this player (the app removes codes already redeemed/used by that account).</summary>
    public Func<string, IReadOnlyList<string>>? CodesFor { get; set; }
    /// <summary>(player, code, outcome, toast text) after each attempt.</summary>
    public Action<string, string, CodeOutcome, string>? CodeTried { get; set; }

    private List<string>? _codes;
    private string _codesPlayer = "";
    private int _codeIdx, _codeStep, _codeTries, _codeOpenTries;
    private long _codeAt;
    private HashSet<long> _toastBase = [];

    private void ResetCodes()
    {
        _codes = null; _codesPlayer = "";
        _codeIdx = _codeStep = _codeTries = _codeOpenTries = 0;
        _codeAt = 0;
        _toastBase = [];
    }

    private bool CodesPending(GameSnapshot s, AppSettings S)
    {
        if (!S.Farm.AutoRedeemCodes || CodesFor is null) return false;
        if (_codes is null)
        {
            if (string.IsNullOrEmpty(s.LocalPlayer)) return false; // codes are remembered per account → wait for the name
            _codesPlayer = s.LocalPlayer;
            _codes = [.. CodesFor(_codesPlayer)];
            _codeIdx = 0;
            if (_codes.Count > 0) _pending.Add(new NoteAction($"Codes: {_codes.Count} to try for {_codesPlayer} ({string.Join(", ", _codes)})."));
        }
        return _codeIdx < _codes.Count;
    }

    /// <summary>Lobby: open the Codes menu (LobbyHUD Codes button) while codes are pending. Null = nothing to do.</summary>
    private List<MacroAction>? OpenCodes(GameSnapshot s, AppSettings S, long now)
    {
        if (!CodesPending(s, S) || s.CodesButton is not { } b) return null;
        if (_codeOpenTries >= 3)
        {
            _codeIdx = _codes!.Count;
            return Note("codes-open-fail", now, 60000, "Codes menu did not open after 3 clicks → codes skipped for this run.");
        }
        Phase = MacroPhase.Lobby;
        Status = "Opening Codes.";
        if (!Ready("codes-open", now, 3000)) return [];
        _codeOpenTries++;
        return [new ClickAction(b.CenterX, b.CenterY, "Codes button")];
    }

    /// <summary>Codes menu: click the box, type the code, check the box text in memory, Redeem, read the new toast. Then close (Exit).</summary>
    private List<MacroAction> CodesMenu(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Lobby;
        _codeOpenTries = 0;
        if (!CodesPending(s, S))
        {
            Status = "Codes done → closing the Codes menu.";
            if (s.CodesExit is not { } x) return Note("codes-noexit", now, 10000, "Codes menu open but its Exit button is not readable → waiting.");
            return Ready("codes-close", now, 2000) ? [new ClickAction(x.CenterX, x.CenterY, "Codes Exit")] : [];
        }
        string code = _codes![_codeIdx];
        Status = $"Code {_codeIdx + 1}/{_codes.Count}: {code}";
        switch (_codeStep)
        {
            case 0:
                if (s.CodesTextBox is not { } box) return Note("codes-nobox", now, 10000, "Codes menu open but the code box is not readable → waiting.");
                _codeStep = 1; _codeAt = now;
                return [new ClickAction(box.CenterX, box.CenterY, "Code box")];
            case 1:
                if (now - _codeAt < 300) return [];
                _codeStep = 2; _codeAt = now;
                // 1st try: Unicode characters (no Shift); 2nd try: plain keys.
                return [new TypeTextAction(code, Unicode: _codeTries == 0, $"Type code {code}")];
            case 2:
                if (now - _codeAt < 500) return [];
                // v0.3.27 (Byzan log 18:30:08-12): while the box has focus its Text reads '' in memory although the code is on screen.
                // Empty = not readable while focused → Redeem anyway (box + button are proven); the toast decides. Other text = real mismatch.
                bool match = string.Equals(s.CodesText?.Trim(), code, StringComparison.OrdinalIgnoreCase);
                bool unreadable = string.IsNullOrEmpty(s.CodesText?.Trim());
                if (match || unreadable)
                {
                    if (unreadable) _pending.Add(new NoteAction($"Code box reads '' while focused → Redeem {code}, result from the message."));
                    if (s.CodesRedeem is not { } r) return Note("codes-noredeem", now, 10000, "Redeem button not readable → waiting.");
                    _toastBase = s.Notifications.Select(n => n.Id).ToHashSet();
                    _codeStep = 3; _codeAt = now;
                    return [new ClickAction(r.CenterX, r.CenterY, $"Redeem {code}")];
                }
                if (now - _codeAt < 1500) return [];
                if (++_codeTries >= 2)
                    return NextCode(code, CodeOutcome.Failed, $"box text '{s.CodesText ?? "?"}' ≠ code after 2 tries (typing not accepted)");
                _codeStep = 0;
                return Note("codes-retype", now, 1000, $"Code box shows '{s.CodesText ?? "?"}' instead of {code} → typing again with plain keys.");
            default:
                var toast = s.Notifications.FirstOrDefault(n => !_toastBase.Contains(n.Id));
                if (toast is not null) return NextCode(code, CodeList.Classify(toast.Text), toast.Text);
                if (now - _codeAt < 6000) return [];
                return NextCode(code, CodeOutcome.NoResponse, "no message within 6 s");
        }
    }

    private List<MacroAction> NextCode(string code, CodeOutcome outcome, string text)
    {
        CodeTried?.Invoke(_codesPlayer, code, outcome, text);
        _codeIdx++; _codeStep = 0; _codeTries = 0;
        return [new NoteAction($"Code {code}: {outcome} ({text}).")];
    }
}

using SupaZero.Core.Game;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

/// <summary>Action calibration result: maps per mode (locked/unlocked) and the act list per map, all from the GUI as read.</summary>
public sealed record CalibrationScan(IReadOnlyList<KnownMap> Maps, IReadOnlyDictionary<string, List<int>> Acts, IReadOnlyList<string> Notes)
{
    /// <summary>v0.3.4: targetable materials (TargetPill shown on Hard/Nightmare) per "Map|Act N".</summary>
    public IReadOnlyDictionary<string, List<string>> Drops { get; init; } = new Dictionary<string, List<string>>();
}

/// <summary>
/// v0.2.82 Action calibration (Byzan's request 25 Oct 2026): the character performs actions to OBTAIN data, not guess it.
/// Lobby: open the Trait menu, then Spin (power data) → walk to the Create Room room → each mode: click the mode, read the map list →
/// each unlocked map: click the map, read ActsList → click Leave (cancel, never clicks Start/difficulty).
/// Each new reading is accepted once it stays exactly the same for <see cref="CalStableMs"/> (GUI lists change gradually after a click).
/// </summary>
public sealed partial class FarmMachine
{
    public bool Calibrating { get; private set; }
    public Action<CalibrationScan>? CalibrationFinished { get; set; }
    public const int CalStableMs = 250, CalStepTimeoutMs = 10_000; // v0.3.7: 800→400 ms; v0.3.35 (Byzan 27 Oct 23:47 "too slow"): 400→250 ms

    // 1 read mode list · 2 pick mode · 3 read maps · 4 pick map · 5 read acts · 6 Leave
    private int _calStage;
    private List<string> _calModes = [];
    private int _calModeIdx;
    private List<string> _calMaps = [];
    private int _calMapIdx;
    private readonly List<KnownMap> _calKnown = [];
    private readonly Dictionary<string, List<int>> _calActs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _calNotes = [];
    private string? _calSig;
    // v0.3.4 drops per act: 7 pick Hard/Nightmare · 8 each unlocked act: click the act, read Rewards (TargetPill).
    private readonly Dictionary<string, List<string>> _calDrops = new(StringComparer.OrdinalIgnoreCase);
    private List<int> _calDropActs = [];
    private int _calDropIdx;
    private Difficulty _calDiffTry = Difficulty.Nightmare;
    public const long CalDiffTimeoutMs = 4000, CalActTimeoutMs = 6000;
    public static string DropKey(string map, int act) => $"{map.Trim()}|Act {act}";
    private long _calSigSince, _calStepAt = -1;
    // v0.3.7 (Byzan 25 Oct 09:03): the map list contains all modes → single pass, ordered by list position (top→bottom).
    private bool _calAllListed;
    // Trials last: click the card → Trials panel (Floor Rewards) → read drops → Leave Trials (also exits the room menu).
    private string? _calTrials;
    private long? _calTrialsClickAt;
    private bool _calTrialsRead;
    public const int CalTrialsOpenMs = 4000;

    public void StartCalibration()
    {
        Calibrating = true;
        _calStage = 0; _calModeIdx = _calMapIdx = 0;
        _calModes = []; _calMaps = [];
        _calKnown.Clear(); _calActs.Clear(); _calNotes.Clear(); _calDrops.Clear(); _calDropActs = []; _calDropIdx = 0;
        _calSig = null; _calStepAt = -1;
        _calAllListed = false; _calTrials = null; _calTrialsClickAt = null; _calTrialsRead = false;
        _pcStep = 1; _pcStepAt = null; _pcOpenAt = null; // v0.3.12: power check via the "Spinning" button (SummonButton) → Roll → ExitButton
        _trStep = 0; _trAt = null; _trHeader = null; _trClickAt = null; _trSeen.Clear();
    }

    // ---- Trait (Byzan 25 Oct 2026): Areas → "Traits" card → teleport in front of NPC Matilda → walk forward (W) → Trait Reroll panel
    // (SurfaceGui "UI Animation (Working).Trait Reroll") opens → read → walk back (S) until the panel closes. Never clicks anything in the panel.
    private int _trStep;
    private long? _trAt;
    private string? _trHeader;
    private long _trHeaderAt;
    private long? _trClickAt;
    private readonly HashSet<int> _trSeen = [];
    public const int TraitNextTimeoutMs = 2500, TraitReadMs = 400; // v0.3.13: 1500→400 (Trait screen is read every tick)

    /// <summary>"Shadow Lord 3/5" → (3, 5); no counter → (0, 0).</summary>
    public static (int Cur, int Total) TraitCounter(string? header)
    {
        var m = header is null ? null : System.Text.RegularExpressions.Regex.Match(header, @"(\d+)\s*/\s*(\d+)\s*$");
        return m is { Success: true } ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : (0, 0);
    }
    public const int TraitTeleportSettleMs = 1200, TraitWalkMs = 10_000, TraitBackMs = 6000;
    public const string CalRoomCard = "Story";
    public const double TraitSpotMaxDist = 25; // Traits card spawn ↔ GuideSpot ≈ 7 studs (RIE)

    private List<MacroAction>? CalTraits(GameSnapshot s, long now)
    {
        if (_trStep >= 5) return null;
        var stand = new MoveAction([], "Trait calibration");
        _trAt ??= now;
        long dt = now - _trAt.Value;
        void Go(int st) { _trStep = st; _trAt = now; }
        List<MacroAction> Skip(string why) { _calNotes.Add(why); _trStep = 5; _trAt = null; return [stand, new NoteAction("Calibration: " + why)]; }
        if (s.TraitMenuOpen && _trStep < 3) Go(3);
        switch (_trStep)
        {
            case 0:
            {
                Status = "Trait calibration: Areas → Traits.";
                var card = s.TeleportCards.FirstOrDefault(c => c.Name.Equals(AnimeZeroPaths.TraitsCard, StringComparison.OrdinalIgnoreCase));
                if (card is not null)
                {
                    var r = ClickEntry(s, card, s.AreasView, "cal-traits", "Teleport Traits", now) ?? [];
                    if (r.Any(a => a is ClickAction)) Go(1);
                    return [stand, .. r];
                }
                if (dt > 15_000) return Skip($"Traits card in the Areas panel not readable (read: {(s.TeleportCards.Count > 0 ? string.Join(", ", s.TeleportCards.Select(c => c.Name)) : "-")}) → trait not checked.");
                if (s.TeleportCards.Count == 0 && s.AreasButton is { } ab && InWindow(s, ab.CenterX, ab.CenterY))
                    return Ready("cal-areas", now, 2500) ? [stand, new ClickAction(ab.CenterX, ab.CenterY, "Areas button (trait calibration)")] : [stand];
                return [stand];
            }
            case 1:
                Status = "Trait calibration: waiting for the teleport to Matilda.";
                if (s.TeleportCards.Count == 0 && dt >= TraitTeleportSettleMs) { Go(2); return CalTraits(s, now); }
                if (dt > 10_000) return Skip("Areas panel did not close after clicking Traits.");
                return [stand];
            case 2:
            {
                // v0.2.87: walk to GuideSpots.Enchants (position proven via RIE), direction from character position + camera; not blind W.
                Status = "Trait calibration: walking to the Enchants spot (Matilda).";
                if (s.TraitSpot is not { } spot) return Skip("Systems.GuideSpots.Enchants spot not readable → not walking.");
                if (s.PlayerPos is not { } pp) return Skip("character position not readable → not walking.");
                double d = Navigator.Distance2D(pp.X, pp.Z, spot.X, spot.Z);
                if (d > TraitSpotMaxDist) return Skip($"Enchants spot is {d:0} studs from the character (> {TraitSpotMaxDist}) → Traits teleport not proven, not walking.");
                if (dt > TraitWalkMs) return Skip($"Trait Reroll panel did not open within {TraitWalkMs / 1000} s (distance to spot {d:0.#} studs).");
                var walk = WalkTo(s, spot, 1.0, now, "Enchants spot", attackWhenStuck: false);
                return walk.Count > 0 ? walk : [stand];
            }
            case 3:
            {
                Status = "Trait calibration: Trait Reroll panel open, reading…";
                if (!s.TraitMenuOpen) return Skip("Trait Reroll panel closed before it was read.");
                // v0.3.6: read ALL units: wait for the trait read (ReadPowers every 2 s), then click ">" (proven via RIE); the header must change.
                var (cur, total) = TraitCounter(s.TraitHeader);
                if (s.TraitHeader != _trHeader) { _trHeader = s.TraitHeader; _trHeaderAt = now; _trClickAt = null; }
                if (cur > 0 && total > 0)
                {
                    if (now - _trHeaderAt < TraitReadMs) return [stand];
                    if (_trSeen.Add(cur)) _calNotes.Add($"trait for unit {s.TraitHeader} read");
                    if (_trSeen.Count < total)
                    {
                        if (_trClickAt is long ca && now - ca > TraitNextTimeoutMs)
                            return Skip($"clicking \">\" did not change the unit (still '{s.TraitHeader}') → {_trSeen.Count}/{total} units read.");
                        if (_trClickAt is not null) return [stand];
                        if (s.TraitNext is not { } nx || !InWindow(s, nx.CenterX, nx.CenterY))
                            return Skip($"Trait panel \">\" button not readable → {_trSeen.Count}/{total} units read.");
                        _trClickAt = now;
                        Status = $"Trait calibration: {s.TraitHeader} → next unit (>).";
                        return [stand, new ClickAction(nx.CenterX, nx.CenterY, $"Trait > ({cur}/{total})")];
                    }
                }
                else if (dt < PowerMenuReadMs) return [stand];
                string eq = s.Equipped.Count == 0 ? "slots not read yet" : string.Join(" · ", s.Equipped.OrderBy(p => p.Loadout).Select(p =>
                    $"slot {p.Loadout} {p.Name}{(p.Level is { } lv ? $" Lv.{lv}" : "")}{(p.Trait is { } t ? $" ({t.Name})" : "")}"));
                Go(4);
                return [stand, new NoteAction($"Trait calibration: {eq}.")];
            }
            default:
            {
                // Leave the Enchants zone: walk to the Traits teleport spot (AreasTeleportLocation.Traits, proven via RIE to be outside the zone).
                Status = "Trait calibration: leaving the zone until the panel closes.";
                if (!s.TraitMenuOpen) { _trStep = 5; _trAt = null; return [stand]; }
                if (s.TraitExit is not { } exit) return Skip("AreasTeleportLocation.Traits spot not readable → Trait panel not closed, close it yourself (X).");
                if (dt > TraitBackMs) return Skip("Trait Reroll panel did not close after leaving the zone → close it yourself (X).");
                var walk = WalkTo(s, exit, 1.0, now, "outside the Enchants zone", attackWhenStuck: false);
                return walk.Count > 0 ? walk : [stand];
            }
        }
    }

    /// <summary>Status prefix added only once (v0.2.84: a Status not refreshed every tick stacked up as "Calibration: Calibration: …").</summary>
    private void CalPrefix() { if (!Status.StartsWith("Calibration", StringComparison.Ordinal)) Status = "Calibration: " + Status; }

    private bool CalStable(string sig, long now)
    {
        if (sig != _calSig) { _calSig = sig; _calSigSince = now; return false; }
        return now - _calSigSince >= CalStableMs;
    }

    private void CalGo(int stage, long now) { _calStage = stage; _calSig = null; _calStepAt = now; }
    private bool CalTimedOut(long now) => _calStepAt >= 0 && now - _calStepAt > CalStepTimeoutMs;
    private string CalMode => _calModeIdx < _calModes.Count ? _calModes[_calModeIdx] : "";

    private List<MacroAction> CalSkip(string note, int stage, long now)
    {
        _calNotes.Add(note);
        CalGo(stage, now);
        return [new NoteAction("Calibration: " + note)];
    }

    private List<MacroAction> CalibrationStep(GameSnapshot s, AppSettings S, long now)
    {
        if (_calStepAt < 0) _calStepAt = now;
        switch (s.Screen)
        {
            case GameScreen.Unknown:
                return Wait("Calibration: waiting for Roblox / DataModel.");
            case GameScreen.Lobby:
                if (_calStage == 9 && !_calTrialsRead && _calTrialsClickAt is long tc && now - tc < CalTrialsOpenMs)
                    return [new MoveAction([], "Calibration: waiting for the Trials panel")];
                if (_calStage >= 6) return CalFinish(_calStage == 9 ? "Trials panel closed via Leave" : "room menu closed via Leave");
                if (CalTraits(s, now) is { } tr) return tr;
                if (_calStage == 0 && PowerCheck(s, now) is { } pc) return pc;
                if (_calStage > 0) return CalFinish("room menu closed before finishing");
                // v0.2.89: the Create Room circle is in the Story area (Byzan 25 Oct) → calibration always uses the "Story" card, not the farm mode.
                var walk = WalkToLobby(s, S, now, force: true, cardName: CalRoomCard);
                CalPrefix();
                return walk;
            case GameScreen.LobbyMenu:
                return CalMenu(s, now);
            case GameScreen.TrialsMenu when _calStage > 0:
                return CalTrialsPanel(s, now);
            default:
                Calibrating = false;
                return Stop($"Calibration can only be started from the lobby (current screen: {s.Screen}).");
        }
    }

    private List<MacroAction> CalMenu(GameSnapshot s, long now)
    {
        Phase = MacroPhase.Lobby;
        var stand = new MoveAction([], "Calibration");
        if (_calStage == 0) CalGo(1, now);

        if (_calStage == 1)
        {
            Status = "Calibration: reading the mode list.";
            string sig = string.Join("|", s.LobbyModes.Select(m => $"{m.Name}:{m.Locked}"));
            if (!CalStable(sig, now))
                return CalTimedOut(now) ? CalSkip("mode list not stable → selected mode only.", 1, now) : [stand];
            _calModes = s.LobbyModes.Where(m => !m.Locked).Select(m => m.Name).ToList();
            if (_calModes.Count == 0) _calModes = [""]; // no mode list: use only the currently selected one
            _calModeIdx = 0;
            CalGo(2, now);
        }

        if (_calStage == 2)
        {
            if (_calModeIdx >= _calModes.Count) { CalGo(_calTrials is not null ? 9 : 6, now); }
            else
            {
                string m = CalMode;
                var e = m.Length == 0 ? null : s.LobbyModes.FirstOrDefault(x => x.Name == m);
                if (m.Length == 0 || e is { Selected: true }) CalGo(3, now);
                else if (e is null || CalTimedOut(now)) { _calModeIdx++; return CalSkip($"mode {m} cannot be selected → skipped.", 2, now); }
                else
                {
                    Status = $"Calibration: selecting mode {m}.";
                    return [stand, .. ClickEntry(s, e, s.LobbyModesView, "cal-mode", $"Mode {m}", now) ?? []];
                }
            }
        }

        if (_calStage == 3)
        {
            Status = $"Calibration: reading maps for mode {(CalMode.Length > 0 ? CalMode : "selected")}.";
            string sig = string.Join("|", s.LobbyMaps.Select(x => $"{x.Name}:{x.Locked}:{x.Mode}"));
            if (s.LobbyMaps.Count == 0 || !CalStable(sig, now))
            {
                if (!CalTimedOut(now)) return [stand];
                _calModeIdx++;
                return CalSkip($"map list for mode {CalMode} not readable → skipped.", 2, now);
            }
            foreach (var x in s.LobbyMaps)
            {
                _calKnown.RemoveAll(k => k.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase));
                _calKnown.Add(new KnownMap(x.Name, x.Locked, x.Mode ?? (CalMode.Length > 0 ? CalMode : null)));
            }
            // "Trials" opens the Trials panel (not acts) → calibrated LAST because its Leave closes the room menu.
            if (s.LobbyMaps.FirstOrDefault(x => !x.Locked && OpensOtherPanel(x.Name)) is { } tri) _calTrials = tri.Name;
            // v0.3.7: ordered by list position (top→bottom) so scrolling goes one way; the list holds maps across modes → other modes need not be clicked.
            _calAllListed = s.LobbyMaps.Select(x => x.Mode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2;
            _calMaps = s.LobbyMaps.Where(x => !x.Locked && !_calActs.ContainsKey(x.Name) && !OpensOtherPanel(x.Name))
                .OrderBy(x => x.Rect is null ? 1 : 0).ThenBy(x => x.Rect?.Y ?? 0).Select(x => x.Name).ToList();
            _calMapIdx = 0;
            CalGo(4, now);
        }

        if (_calStage == 4)
        {
            if (_calMapIdx >= _calMaps.Count) { _calModeIdx = _calAllListed ? _calModes.Count : _calModeIdx + 1; CalGo(2, now); return [stand]; }
            string map = _calMaps[_calMapIdx];
            if (Matches(s.LobbyMapTag, map)) CalGo(5, now);
            else
            {
                var e = s.LobbyMaps.FirstOrDefault(x => x.Name == map);
                if (e is null || CalTimedOut(now)) { _calMapIdx++; return CalSkip($"map {map} cannot be selected → its acts not read.", 4, now); }
                Status = $"Calibration: selecting map {map}.";
                return [stand, .. ClickEntry(s, e, s.LobbyMapsView, $"cal-map-{map}", $"Map {map}", now) ?? []];
            }
        }

        if (_calStage == 5)
        {
            string map = _calMaps[_calMapIdx];
            Status = $"Calibration: reading acts for map {map}.";
            var nums = s.LobbyActs.Select(a => ActNumber(a.Name)).Where(n => n > 0).Distinct().Order().ToList();
            string sig = $"{s.LobbyMapTag}|{string.Join(",", s.LobbyActs.Select(a => $"{a.Name}:{a.Locked}"))}";
            if (nums.Count == 0 || !Matches(s.LobbyMapTag, map) || !CalStable(sig, now))
            {
                if (!CalTimedOut(now)) return [stand];
                _calMapIdx++;
                return CalSkip($"acts for map {map} not readable.", 4, now);
            }
            _calActs[map] = nums;
            int locked = s.LobbyActs.Count(a => a.Locked);
            var note = new NoteAction($"Calibration: {map} → Act {string.Join(", ", nums)}{(locked > 0 ? $" ({locked} locked)" : "")}.");
            // v0.3.4: continue reading drops for each unlocked act (needs Hard/Nightmare so TargetPill shows).
            _calDropActs = s.LobbyActs.Where(a => !a.Locked).Select(a => ActNumber(a.Name)).Where(n => n > 0).Distinct().Order().ToList();
            _calDropIdx = 0;
            _calDiffTry = Difficulty.Nightmare;
            if (_calDropActs.Count == 0 || s.LobbyDifficulties.Count == 0)
            {
                if (_calDropActs.Count > 0) _calNotes.Add($"difficulty buttons not readable → drops for {map} not calibrated.");
                _calMapIdx++;
                CalGo(4, now);
            }
            else CalGo(7, now);
            return [stand, note];
        }

        if (_calStage == 7)
        {
            string map = _calMaps[_calMapIdx];
            string? tag = s.LobbyDifficultyTag?.Trim('[', ']', ' ');
            if (tag is not null && (tag.Equals("Hard", StringComparison.OrdinalIgnoreCase) || tag.Equals("Nightmare", StringComparison.OrdinalIgnoreCase))) { CalGo(8, now); }
            else if (now - _calStepAt > CalDiffTimeoutMs)
            {
                if (_calDiffTry == Difficulty.Nightmare) { _calDiffTry = Difficulty.Hard; CalGo(7, now); return [stand]; }
                _calMapIdx++;
                return CalSkip($"Hard/Nightmare cannot be selected on {map} (selected: {tag ?? "?"}) → drops not calibrated.", 4, now);
            }
            else
            {
                Status = $"Drop calibration: selecting {_calDiffTry} ({map}).";
                if (DifficultyPoint(s, _calDiffTry) is not var (dx, dy) || !InWindow(s, dx, dy)) return [stand];
                return Ready("cal-diff", now, 1500) ? [stand, new ClickAction(dx, dy, $"Difficulty {_calDiffTry} (drop calibration)")] : [stand];
            }
        }

        // v0.3.36 (Byzan 27 Oct 23:52 "keep reading saved drops, just faster"): log 14:20 showed act clicks exactly 2.5 s apart
        // (14:20:14.879 → 17.405 → 19.919) although each act's Rewards were read ~1.2 s after its click: the 2.5 s click cooldown
        // was shared by every act/map ("cal-act"/"cal-map"). The cooldown key is now per map/act, so the next act is clicked as soon as
        // the previous one is read; a repeated click on the SAME entry still waits 2.5 s.
        if (_calStage == 8)
        {
            string map = _calMaps[_calMapIdx];
            if (_calDropIdx >= _calDropActs.Count) { _calMapIdx++; CalGo(4, now); return [stand]; }
            int act = _calDropActs[_calDropIdx];
            if (ActNumber(s.LobbyActTag) != act)
            {
                var e = s.LobbyActs.FirstOrDefault(a => ActNumber(a.Name) == act && !a.Locked);
                if (e is null || now - _calStepAt > CalActTimeoutMs) { _calDropIdx++; return CalSkip($"{map} Act {act} cannot be selected → drops not read.", 8, now); }
                Status = $"Drop calibration: selecting {map} Act {act}.";
                return [stand, .. ClickEntry(s, e, s.LobbyActsView, $"cal-act-{map}-{act}", $"Act {act} (drop calibration)", now) ?? []];
            }
            Status = $"Drop calibration: reading Rewards for {map} Act {act}.";
            string sig = $"{s.LobbyActTag}|{s.LobbyDifficultyTag}|{string.Join(",", s.LobbyRewards.Select(r => $"{r.Name}:{r.Targetable}"))}";
            if (s.LobbyRewards.Count == 0 || !CalStable(sig, now))
            {
                if (now - _calStepAt <= CalActTimeoutMs) return [stand];
                _calDropIdx++;
                return CalSkip($"Rewards for {map} Act {act} not readable.", 8, now);
            }
            var names = s.LobbyRewards.Where(r => r.Targetable).Select(r => r.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _calDrops[DropKey(map, act)] = names;
            _calDropIdx++;
            CalGo(8, now);
            return [stand, new NoteAction($"Drop calibration: {map} Act {act} ({s.LobbyDifficultyTag?.Trim('[', ']', ' ')}) → {(names.Count > 0 ? string.Join(", ", names) : "no targetable materials")}.")];
        }

        if (_calStage == 9)
        {
            // Trials last: click the Trials card (scroll if needed) → the TrialsMenu screen is handled by CalTrialsPanel.
            var e = s.LobbyMaps.FirstOrDefault(x => x.Name == _calTrials && !x.Locked);
            if (e is null || (CalTimedOut(now) && _calTrialsClickAt is null)) { _calTrials = null; return CalSkip("Trials card cannot be clicked → Trials drops not calibrated.", 6, now); }
            if (_calTrialsClickAt is long tc && now - tc > CalTrialsOpenMs) { _calTrials = null; return CalSkip("Trials panel did not open after the click → Trials drops not calibrated.", 6, now); }
            Status = "Calibration: opening Trials (last).";
            var click = ClickEntry(s, e, s.LobbyMapsView, "cal-map", "Map Trials (calibration)", now) ?? [];
            if (click.Any(a => a is ClickAction)) _calTrialsClickAt = now;
            return [stand, .. click];
        }

        // 6) Cancel the room: click Leave, never Start.
        Status = "Calibration finished reading → Leave (cancel room).";
        if (s.LobbyLeave is not { } lv)
            return CalFinish("Leave button not readable → close the room menu yourself");
        if (CalTimedOut(now)) return CalFinish("room menu did not close after clicking Leave");
        if (!InWindow(s, lv.CenterX, lv.CenterY)) return CalFinish("Leave button is outside the Roblox window → close the room menu yourself");
        return Ready("cal-leave", now, 3000) ? [stand, new ClickAction(lv.CenterX, lv.CenterY, "Leave (cancel calibration room)")] : [stand];
    }

    /// <summary>
    /// v0.3.7 Trials panel (snapshot s0819 / Byzan's photo 25 Oct 09:03): Floor Rewards tab → Rewards stable → save TargetPill materials
    /// as "Trials|Act &lt;floor&gt;" → click Leave Trials (Buttons.LeaveButton.LeaveButton, text "Leave"), which also closes the room menu.
    /// Never clicks Start, +/-, or materials.
    /// </summary>
    private List<MacroAction> CalTrialsPanel(GameSnapshot s, long now)
    {
        Phase = MacroPhase.Lobby;
        var stand = new MoveAction([], "Calibration");
        var t = s.Trials!;
        if (_calStage != 9) { _calStage = 9; _calSig = null; _calStepAt = now; }
        _calTrialsClickAt = null;
        if (!_calTrialsRead)
        {
            if (CalTimedOut(now)) { _calTrialsRead = true; _calNotes.Add("Trials drops not readable (timeout)."); _calStepAt = now; }
            else if (!t.FloorTabOpen)
            {
                Status = "Trials calibration: opening the Floor Rewards tab.";
                if (t.FloorTab is not { } tab || !InWindow(s, tab.CenterX, tab.CenterY)) return [stand];
                return Ready("cal-tr-tab", now, 1200) ? [stand, new ClickAction(tab.CenterX, tab.CenterY, "Floor Rewards tab (calibration)")] : [stand];
            }
            else
            {
                Status = "Trials calibration: reading Floor Rewards.";
                string sig = $"{t.SelectedFloor}|{string.Join(",", t.Rewards.Select(r => $"{r.Name}:{r.Targetable}"))}";
                if (t.SelectedFloor is not int floor || t.Rewards.Count == 0 || !CalStable(sig, now)) return [stand];
                var names = t.Rewards.Where(r => r.Targetable).Select(r => r.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _calDrops[DropKey("Trials", floor)] = names;
                _calTrialsRead = true; _calStepAt = now;
                return [stand, new NoteAction($"Drop calibration: Trials Floor {floor} → {(names.Count > 0 ? string.Join(", ", names) : "no targetable materials")}.")];
            }
        }
        Status = "Trials calibration finished → Leave.";
        if (t.Leave is not { } lv || !InWindow(s, lv.CenterX, lv.CenterY)) return CalFinish("Leave Trials button not readable → close the Trials panel yourself");
        if (CalTimedOut(now)) return CalFinish("Trials panel did not close after clicking Leave");
        return Ready("cal-leave", now, 3000) ? [stand, new ClickAction(lv.CenterX, lv.CenterY, "Leave Trials (calibration)")] : [stand];
    }

    /// <summary>Map list entry proven to open another panel (Byzan's log 25 Oct 2026 00:14: clicking "Trials" → TrialsMenu).</summary>
    public static bool OpensOtherPanel(string map) => map.Trim().Equals("Trials", StringComparison.OrdinalIgnoreCase);

    private List<MacroAction> CalFinish(string how)
    {
        Calibrating = false;
        var scan = new CalibrationScan(_calKnown.ToList(), new Dictionary<string, List<int>>(_calActs, StringComparer.OrdinalIgnoreCase), _calNotes.ToList())
        { Drops = new Dictionary<string, List<string>>(_calDrops, StringComparer.OrdinalIgnoreCase) };
        CalibrationFinished?.Invoke(scan);
        string acts = scan.Acts.Count == 0 ? "acts not read" : string.Join("; ", scan.Acts.Select(kv => $"{kv.Key} Act {string.Join(",", kv.Value)}"));
        return Stop($"Calibration finished ({how}): {scan.Maps.Count} maps ({scan.Maps.Count(m => !m.Locked)} unlocked) · {acts} · drops for {scan.Drops.Count} acts.");
    }
}

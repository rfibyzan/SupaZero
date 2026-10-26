using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>Circle farther than this → teleport first via the Areas button.</summary>
    public const double TeleportIfFartherThan = 45;
    /// <summary>The Create Room circle must actually be stepped on (JoinHitbox 18 studs) → fixed radius, does not follow "Arrive radius".</summary>
    public const double LobbyArriveRadius = 3;

    /// <summary>
    /// Lobby: nearest "Start Here" circle ≤ 45 studs → walk there. Farther / not read → Areas button →
    /// teleport card (Story) → spawn near the circle. Manually recorded position is only a fallback.
    /// </summary>
    private readonly Dictionary<string, long> _roomBan = new(StringComparer.Ordinal);
    private string? _roomTarget;
    private long? _teleportAt;
    private long? _areasClickAt;      // v0.3.8: teleport card is only clicked after the Areas button was clicked (≤ 6 s)
    private Vec3? _tpFrom;            // position when the teleport card was clicked (to verify the teleport actually happened)
    private long? _arrivedAt;
    /// <summary>v0.3.1 (Byzan screenshot 08:24, 25 Oct): character already on the circle but the read distance stays ~11 studs, menu does not open,
    /// macro keeps "walking" → never enters the JoinTimeout count. Distance does not improve by ≥ 1 stud for this long → treat as failed, switch circle.</summary>
    private string? _progTarget;
    private double _progBest;
    private long _progBestAt;

    /// <summary>v0.3.8: stall limit = the "Switch circle if join fails" setting (s), not a fixed 6 s.</summary>
    private bool RoomStalled(string id, double dist, long now, long limitMs)
    {
        if (_progTarget != id) { _progTarget = id; _progBest = dist; _progBestAt = now; return false; }
        if (dist < _progBest - 1) { _progBest = dist; _progBestAt = now; return false; }
        return now - _progBestAt >= limitMs;
    }

    private List<MacroAction> BanRoom(string id, long now, string why)
    {
        _roomBan[id] = now + 30_000;
        _roomTarget = null;
        _arrivedAt = null;
        _progTarget = null;
        Status = "Join failed, switching circle.";
        return [new MoveAction([], "Switch circle"), new NoteAction($"Cannot join {Label(id)} ({why}) → trying another circle.")];
    }

    private static string Label(string id) => id.StartsWith("room:") ? "room " + id[5..] : id == "manual" ? "recorded circle" : "circle";

    private List<MacroAction> WalkToLobby(GameSnapshot s, AppSettings S, long now, bool force = false, string? cardName = null)
    {
        var f = S.Farm;
        // v0.3.2 (Byzan 25 Oct 08:30): all Create Room circles are in the Story area → always teleport via the "Story" card
        // (MATCHES_Row1.Story.Story), whatever the farm mode (Raid/Infinite/...). The mode is picked later in the Create Room menu.
        string want = cardName ?? CalRoomCard;
        if (!force && !f.AutoCreateLobby) return Wait("In lobby (Auto create room off).");
        if (!force && S.Upgrade.Enabled && !_upgradeDoneThisStage && LobbyUpgrade(s, S.Upgrade, now) is { } lu) return lu;
        if (!force && f.CheckPowersInLobby && PowerCheck(s, now) is { } pc) return pc;
        Phase = MacroPhase.WalkingToLobby;

        // Candidates: QuqueRooms rooms (skip ones occupied by other players), "Start Here" fallback, then the manual recording.
        var cands = new List<(string Id, Vec3 Pos)>();
        foreach (var r in s.StartRooms)
        {
            if (f.SkipOccupiedRooms && r.Occupied) continue;
            cands.Add(("room:" + r.Id, r.Pos));
        }
        if (s.StartRooms.Count == 0)
            for (int i = 0; i < s.StartCircles.Count; i++) cands.Add(("circle:" + i, s.StartCircles[i]));
        if (f.LobbyCircle is { } manual) cands.Add(("manual", manual));

        // Just teleported (≤ 30 s): spawn can be somewhat far from the circle → keep walking (up to 90 studs), do not re-teleport.
        // v0.3.8 (Byzan 25 Oct 10:18): after teleport, a circle whose position is read from memory is approached at any distance
        // (previously capped at 90 studs → stood still and kept re-teleporting).
        bool justTeleported = _teleportAt is long ta && now - ta < 30_000;
        double maxWalk = justTeleported ? double.MaxValue : TeleportIfFartherThan;
        long joinMs = Math.Max(1, f.JoinTimeoutSec) * 1000L;

        // Rooms that failed to join (arrived but menu did not open) are skipped for 30 s.
        foreach (var k in _roomBan.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList()) _roomBan.Remove(k);
        var usable = cands.Where(c => !_roomBan.ContainsKey(c.Id)).ToList();
        // All nearby circles have failed before → retry the nearby ones, do not teleport to another area (cause of the Areas → Teleport loop).
        if (s.PlayerPos is { } pp)
        {
            var near = cands.Where(c => Navigator.Distance2D(pp.X, pp.Z, c.Pos.X, c.Pos.Z) <= maxWalk).ToList();
            if (near.Count > 0 && !usable.Any(c => near.Contains(c)))
            {
                foreach (var c in near) _roomBan.Remove(c.Id);
                usable = near;
            }
        }
        if (usable.Count == 0 && cands.Count > 0)
        {
            _roomBan.Clear(); // all tried → start over
            usable = cands;
        }

        (string Id, Vec3 Pos)? target = null;
        double dist = double.MaxValue;
        if (s.PlayerPos is { } p)
        {
            // Stay on the current target room while it is still valid (avoid flip-flopping).
            if (_roomTarget is { } cur && usable.FirstOrDefault(c => c.Id == cur) is { Id: not null } keep)
            {
                target = keep;
                dist = Navigator.Distance2D(p.X, p.Z, keep.Pos.X, keep.Pos.Z);
            }
            else
                foreach (var c in usable)
                {
                    double d = Navigator.Distance2D(p.X, p.Z, c.Pos.X, c.Pos.Z);
                    if (d < dist) { dist = d; target = c; }
                }
        }
        var pre = new List<MacroAction>();
        if (_roomTarget is not null && target?.Id != _roomTarget)
        {
            if (s.StartRooms.Any(r => "room:" + r.Id == _roomTarget && r.Occupied))
                pre.Add(new NoteAction($"{Label(_roomTarget)} taken by another player → switching circle."));
            _roomTarget = null;
            _arrivedAt = null;
        }

        if (target is { } t && dist <= maxWalk)
        {
            _roomTarget = t.Id;
            var walk = WalkTo(s, t.Pos, LobbyArriveRadius, now, "Create Room circle", attackWhenStuck: false);
            if (walk.Count > 0)
            {
                _arrivedAt = null;
                // Circle = barrier (cannot be entered): distance stops shrinking + menu does not open within the join setting → switch.
                if (RoomStalled(t.Id, dist, now, joinMs)) return [.. pre, .. BanRoom(t.Id, now, $"distance stalled at {dist:0.#} studs for {joinMs / 1000} s, menu did not open")];
                return [.. pre, .. walk];
            }

            _arrivedAt ??= now;
            long waited = now - _arrivedAt.Value;
            if (waited >= joinMs)
                return BanRoom(t.Id, now, $"{waited / 1000} s, distance {dist:0.#} studs");
            Status = $"At {Label(t.Id)}, waiting for the Create Room menu ({waited / 1000}/{f.JoinTimeoutSec} s).";
            return [new MoveAction([], "Arrived")];
        }
        _roomTarget = null;
        _arrivedAt = null;
        if (target is { } far)
            pre.AddRange(Note("room-far", now, 5000, $"Nearest {Label(far.Id)} is {dist:0} studs (> {maxWalk:0}) → teleport via Areas."));
        else if (s.PlayerPos is null)
            pre.AddRange(Note("lobby-nopos", now, 5000, "Character position not read in lobby → cannot walk to the circle."));
        else if (cands.Count == 0)
            pre.AddRange(Note("lobby-norooms", now, 5000, "Room circles (QuqueRooms / Start Here) not read."));
        if (target is null && s.StartRooms.Count > 0 && s.StartRooms.All(r => r.Occupied) && f.SkipOccupiedRooms)
        {
            Status = "All rooms occupied by other players, waiting for an empty one.";
            return [.. pre, new MoveAction([], "Wait for empty room"), .. Note("rooms-full", now, 10000, "All rooms occupied by other players, waiting for an empty one.")];
        }

        // Just teleported: wait for character/circle to be read, do not re-teleport (max 1x / 12 s).
        // v0.3.8 (Byzan 25 Oct 10:20 "teleport tab not opened yet"): the card can be read even while the Areas panel is closed → click hits nothing.
        // Proof of teleport = position changed. 3 s without moving ≥ 20 studs → treat as failed, open Areas first.
        if (_teleportAt is long tf && now - tf >= 3000 && _tpFrom is { } from && s.PlayerPos is { } pnow
            && Navigator.Distance2D(pnow.X, pnow.Z, from.X, from.Z) < 20)
        {
            _teleportAt = null; _tpFrom = null; _areasClickAt = null;
            pre.Add(new NoteAction("Teleport did not happen (position unchanged for 3 s) → open Areas first, then click the card again."));
        }
        else if (_teleportAt is long tq && now - tq < 12_000)
        {
            Status = "Waiting after teleport…";
            string why = s.PlayerPos is null ? "character position not read"
                : cands.Count == 0 ? "room circles (QuqueRooms / Start Here) not read yet"
                : "all nearby circles are being skipped";
            return [.. pre, new MoveAction([], "Wait for teleport"), .. Note("tp-wait", now, 4000, $"Waiting after teleport: {why}.")];
        }

        // Teleport via Areas → card named after that mode. v0.2.80: no Story card fallback.
        var card = s.TeleportCards.FirstOrDefault(c => c.Name.Trim().Equals(want, StringComparison.OrdinalIgnoreCase));
        if (card is null && s.TeleportCards.Count > 0)
            return [.. pre, new MoveAction([], "Wait"), .. Note("tp-nocard", now, 8000, $"Teleport card '{want}' not found (read: {string.Join(", ", s.TeleportCards.Select(c => c.Name))}) → not clicked.")];
        bool areasJustClicked = _areasClickAt is long ac && now - ac <= 6000;
        if (card?.Rect is not null && (areasJustClicked || s.AreasButton is null))
        {
            Status = $"Teleport: Areas → {card.Name}";
            // v0.2.90: click only when the card is visible inside the Areas panel (Clipping) and the Roblox window; otherwise scroll/wait.
            var click = ClickEntry(s, card, s.AreasView, "tp-card", $"Teleport {card.Name}", now) ?? [];
            if (click.Any(a => a is ClickAction)) { _teleportAt = now; _tpFrom = s.PlayerPos; _areasClickAt = null; }
            return [.. pre, new MoveAction([], "Teleport"), .. click];
        }
        if (s.AreasButton is { } ab)
        {
            Status = "Opening Areas to teleport.";
            if (areasJustClicked) return [.. pre, new MoveAction([], "Wait for Areas panel")];
            if (!Ready("areas", now, 1500)) return pre;
            _areasClickAt = now;
            return [.. pre, new MoveAction([], "Teleport"), new ClickAction(ab.CenterX, ab.CenterY, "Areas button")];
        }
        Status = "Create Room circle and Areas button not read.";
        return [new MoveAction([], "Wait"), .. Note("no-hub", now, 8000, "Areas button / 'Start Here' circle not read. Check in RIE or record the circle position manually.")];
    }

    private List<MacroAction> LobbyMenu(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Lobby;
        var f = S.Farm;
        if (!f.AutoCreateLobby) return Wait("Create Room menu open (Auto create room off).");

        // 1) Mode
        if (s.LobbyModes.Count > 0 && Pick(s, s.LobbyModes, s.LobbyModesView, f.Mode, "mode", now) is { } modeAct) return modeAct;
        // 2) Map. Empty map = currently selected map, but if that map's mode does not match → first (unlocked) map with the chosen mode.
        string targetMap = f.Map;
        if (string.IsNullOrWhiteSpace(targetMap) && s.LobbyMaps.Any(m => m.Mode is not null))
        {
            var cur = s.LobbyMaps.FirstOrDefault(m => Matches(s.LobbyMapTag, m.Name)) ?? s.LobbyMaps.FirstOrDefault(m => m.Selected);
            if (cur?.Mode is { } cm && !Matches(cm, f.Mode))
            {
                var byMode = s.LobbyMaps.FirstOrDefault(m => !m.Locked && m.Mode is { } mm && Matches(mm, f.Mode));
                if (byMode is null) return Stop($"No unlocked map for mode {f.Mode}.");
                targetMap = byMode.Name;
            }
        }
        f = f with { Map = targetMap };
        if (!string.IsNullOrWhiteSpace(f.Map) && !Matches(s.LobbyMapTag, f.Map))
        {
            Status = $"Selected map '{s.LobbyMapTag ?? "?"}', target '{f.Map}'.";
            var entry = s.LobbyMaps.FirstOrDefault(e => Matches(e.Name, f.Map));
            if (entry is { Locked: true }) return Stop($"Map '{f.Map}' is still locked on this account.");
            if (entry is not null && ClickEntry(s, entry, s.LobbyMapsView, "lobby-map", $"Map {entry.Name}", now) is { } mapAct) return mapAct;
            return Note("lobby-map", now, 5000, $"Map '{f.Map}' not in MapsList (read: {string.Join(", ", s.LobbyMaps.Select(m => m.Name))}).");
        }
        // 3) Act. The act list depends on the map (Training only has Act 1) → target is matched to the acts that EXIST in ActsList.
        string mapKey = (f.Map.Length > 0 ? f.Map : s.LobbyMapTag ?? "").Trim();
        var acts = s.LobbyActs.Select(e => (Entry: e, No: ActNumber(e.Name))).Where(x => x.No > 0).OrderBy(x => x.No).ToList();
        int want = f.Act;
        if (f.Act == 0) // All acts in order: the act after the last completed one
        {
            int done = f.ActProgress?.GetValueOrDefault(mapKey) ?? 0;
            want = done + 1;
            if (acts.Count > 0 && want > acts[^1].No)
                return Stop($"All acts on map '{mapKey}' are completed (Act 1–{acts[^1].No}).");
        }
        // v0.2.80 (no guessing): requested act does not exist → stop, do not switch to another act.
        if (acts.Count > 0 && !acts.Any(a => a.No == want))
            return Stop($"Act {want} not found on map '{mapKey}' (available: {string.Join(", ", acts.Select(a => a.No))}). Change Act in the Farm card.");
        _selectedAct = want;
        _selectedMap = mapKey;
        if (ActNumber(s.LobbyActTag) != want)
        {
            Status = $"Selected act '{s.LobbyActTag ?? "?"}', target Act {want}.";
            var entry = acts.FirstOrDefault(a => a.No == want).Entry;
            if (entry is { Locked: true }) return Stop($"Act {want} on map '{mapKey}' is still locked.");
            if (entry is not null && ClickEntry(s, entry, s.LobbyActsView, "lobby-act", $"Act {want}", now) is { } actAct) return actAct;
            return Note("lobby-act", now, 5000, $"Act {want} not read in ActsList (ActLine.Tag = '{s.LobbyActTag ?? "?"}').");
        }
        string wantAct = "Act " + want;
        // 4) Difficulty (click first, wait until registered) → Start
        Status = $"Create Room: {f.Mode} {(f.Map.Length > 0 ? f.Map : s.LobbyMapTag)} {wantAct} {f.Difficulty} → Start.";
        bool diffKnown = !string.IsNullOrEmpty(s.LobbyDifficultyTag);
        bool diffOk = diffKnown && s.LobbyDifficultyTag!.Equals(f.Difficulty.ToString(), StringComparison.OrdinalIgnoreCase);
        // v0.2.80: the selected difficulty must be read (previously: empty tag → clicked every 8 s without knowing the result).
        if (!diffKnown) return Note("lobby-diff-unknown", now, 5000, "Selected difficulty not read → Start held back.");
        if (!diffOk)
        {
            if (DifficultyPoint(s, f.Difficulty) is not var (dx, dy))
                return Note("lobby-diff", now, 5000, $"Difficulty button {f.Difficulty} not read → not clicked.");
            if (!InWindow(s, dx, dy))
                return Note("lobby-diff", now, 5000, $"Difficulty button {f.Difficulty} outside the Roblox window ({dx:0},{dy:0}) → not clicked.");
            if (!Ready("lobby-diff", now, 1500)) return [];
            _diffAt = now;
            Status = $"Selected difficulty '{s.LobbyDifficultyTag ?? "?"}', target {f.Difficulty}.";
            return [new ClickAction(dx, dy, $"Difficulty {f.Difficulty}")];
        }
        if (_diffAt is { } da && now - da < 700) return [];
        if (DropTarget(s, s.LobbyRewards, s.LobbyRewardsView, f.DropTarget, "lobby-target", now) is { } tgtAct) return tgtAct;
        // v0.2.80: the Start button must be read (1920x1080 fallback coordinates removed).
        if (s.LobbyStart is not { } st) return Note("lobby-start-missing", now, 5000, "Start button not read → not clicked.");
        var (sx, sy) = (st.CenterX, st.CenterY);
        if (!InWindow(s, sx, sy))
            return Note("lobby-start", now, 5000, $"Start button outside the Roblox window ({sx:0},{sy:0}) → not clicked.");
        return Ready("lobby-start", now, 3000) ? [new ClickAction(sx, sy, "Start (Create Room)")] : [];
    }

    private static (double X, double Y)? DifficultyPoint(GameSnapshot s, Difficulty d)
    {
        var hit = s.LobbyDifficulties.FirstOrDefault(e => e.Name.Equals(d.ToString(), StringComparison.OrdinalIgnoreCase));
        return hit?.Rect is { } r ? (r.CenterX, r.CenterY) : null;
    }

    /// <summary>Point is inside the Roblox window client area. v0.2.80: unknown size = NOT inside.</summary>
    public static bool InWindow(GameSnapshot s, double x, double y) =>
        s.ClientW > 0 && s.ClientH > 0 && (x >= 2 && y >= 2 && x <= s.ClientW - 2 && y <= s.ClientH - 2);

    /// <summary>
    /// Click a list entry (map/act/mode). If the entry is outside its list's visible area (needs scrolling) or outside the
    /// window: mouse-wheel scroll over the list toward the entry, re-read, and click only once it is visible. Never
    /// clicks outside the Roblox window. null = entry without a rect.
    /// </summary>
    private List<MacroAction>? ClickEntry(GameSnapshot s, LobbyEntry e, GuiRect? view, string key, string label, long now)
    {
        if (e.Rect is not { } r) return null;
        double margin = view is { } v0 ? Math.Min(8, Math.Min(v0.W, v0.H) / 4) : 0;
        bool inView = view is not { } vw || vw.Contains(r.CenterX, r.CenterY, margin);
        if (inView && InWindow(s, r.CenterX, r.CenterY))
        {
            _scrolls.Remove(key);
            return Ready(key, now, 2500) ? [new ClickAction(r.CenterX, r.CenterY, label)] : [];
        }
        if (view is not { } v)
            return Note(key + "-out", now, 5000, $"{label} outside the Roblox window ({r.CenterX:0},{r.CenterY:0}) and list area not read → not clicked.");

        int n = _scrolls.GetValueOrDefault(key);
        if (n >= 40)
            return Note(key + "-scroll", now, 5000, $"{label} still not visible after {n} scrolls → not clicked. Check the Roblox window size.");
        // Actually visible part of the list = list rect ∩ window.
        double top = v.Y, bottom = v.Bottom, left = v.X, right = v.Right;
        if (s.ClientW > 0 && s.ClientH > 0)
        {
            top = Math.Max(top, 0); bottom = Math.Min(bottom, s.ClientH); left = Math.Max(left, 0); right = Math.Min(right, s.ClientW);
            if (bottom - top < 8 || right - left < 8)
                return Note(key + "-out", now, 5000, $"List for {label} outside the Roblox window → not scrolled/clicked.");
        }
        // Target below/right → scroll down (negative notches); above/left → up.
        bool vertical = r.CenterY < top + margin || r.CenterY > bottom - margin;
        double dist = vertical ? (r.CenterY > bottom - margin ? r.CenterY - (bottom - margin) : r.CenterY - (top + margin))
                               : (r.CenterX > right - margin ? r.CenterX - (right - margin) : r.CenterX - (left + margin));
        double span = Math.Max(40, (vertical ? bottom - top : right - left) * 0.5);
        int notches = (int)Math.Clamp(Math.Ceiling(Math.Abs(dist) / span), 1, 3) * (dist > 0 ? -1 : 1);
        double cx = (left + right) / 2, cy = (top + bottom) / 2;
        if (!Ready(key + "-scroll", now, 450)) return [];
        _scrolls[key] = n + 1;
        Status = $"Scrolling list to {label}.";
        return [new ScrollAction(cx, cy, notches, $"{label} not visible yet → scroll {(notches < 0 ? "down" : "up")}")];
    }

    /// <summary>Click the matching entry if not selected yet. null = already correct / no data.</summary>
    private List<MacroAction>? Pick(GameSnapshot s, IReadOnlyList<LobbyEntry> list, GuiRect? view, string want, string what, long now)
    {
        var entry = list.FirstOrDefault(e => Matches(e.Name, want));
        if (entry is null || entry.Selected) return null;
        if (entry.Locked) return Stop($"{what} '{want}' is still locked on this account.");
        Status = $"Selecting {what} {want}.";
        return ClickEntry(s, entry, view, "lobby-" + what, $"{what} {entry.Name}", now) ?? [];
    }

    private List<MacroAction> Vote(GameSnapshot s, AppSettings S, long now)
    {
        Phase = MacroPhase.Voting;
        Status = "Start Game? → Yes";
        if (!S.Farm.AutoVoteStart) return [];
        // v0.2.79 (no guessing): the Yes button must be read; fallback coordinates are not used.
        if (s.StartVoteYes is not { } r) return Note("vote-yes-missing", now, 5000, "Vote shown but Yes button not read → not clicked.");
        if (!Ready("vote-yes", now, 2000)) return [];
        return [new ClickAction(r.CenterX, r.CenterY, "Vote Yes") { InRound = true }];
    }

    // ---------- Round ----------
}

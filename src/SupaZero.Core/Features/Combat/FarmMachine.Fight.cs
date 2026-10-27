using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    private List<MacroAction> Fight(GameSnapshot s, AppSettings S, long now)
    {
        // v0.3.23 Carry: damage target reached → idle (no attack/skill/move/pickup) until the end screen. Unreadable damage → fight normally.
        if (S.Combat.CarryDamage > 0)
        {
            if (s.MyDamage is { } dmg && dmg >= S.Combat.CarryDamage)
            {
                Status = $"Carry: damage {dmg:N0} ≥ {S.Combat.CarryDamage:N0}, idle until the round ends.";
                return [new MoveAction([], "Carry idle")];
            }
            if (s.MyDamage is null)
                _pending.AddRange(Note("carry-dmg", now, 15000, "Carry: own damage on the scoreboard (RunPlayerList row = UserId) unreadable → fighting normally."));
        }
        var hud = s.Hud;
        int left = s.EnemyCount >= 0 ? s.EnemyCount : hud?.EnemiesLeft ?? -1;
        string area = hud is { AreaTotal: > 0 } ? $"Area {hud.Area}/{hud.AreaTotal}" : "Area ?";
        if (s.HudRaw?.Contains("rubble", StringComparison.OrdinalIgnoreCase) == true)
            _pending.AddRange(Note("rubble-diag", now, 8000, s.RubblePos is { } rp
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Rubble at ({rp.X:0},{rp.Y:0},{rp.Z:0}){(s.RubbleStand is { } rs ? $", stand at ({rs.X:0},{rs.Z:0})" : "")} [{s.RubbleInfo}], player {(s.PlayerPos is { } pp ? $"({pp.X:0},{pp.Z:0})" : "?")}.")
                : $"Rubble active but its position is unreadable: {s.RubbleInfo}."));
        // v0.3.12 (Byzan 26 Oct): Hxh rubble exists from the start; once this area's wave is clear the rubble can be hit even though the HUD does not yet
        // say "Break the rubble". Rubble = RubbleBar Adornee whose Blocker is still solid (evidence v0.2.91), near this area.
        bool waveAlive = s.Enemies.Any(e => e.Alive && e.Name != AnimeZeroPaths.RubbleName);
        if (waveAlive && hud is { Area: > 0, Enter: false }) _waveArea = hud.Area;
        if (left == 0 && s.RubblePending is { } rpend && hud is { Area: > 0 } && _waveArea > 0
            && (hud.Area == _waveArea || (hud.Enter && hud.Area == _waveArea + 1 && RubbleBeforeNextArea(s, rpend))) && RubbleNear(s, rpend))
        {
            _pending.AddRange(Note("rubble-pending", now, 10000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Wave of area {_waveArea} clear → attack rubble at ({rpend.X:0},{rpend.Z:0}) [{s.RubbleInfo}].")));
            s = s with { Enemies = [new EnemyInfo(AnimeZeroPaths.RubbleName, rpend, Math.Max(0.01, s.RubbleHp ?? 1), 1)], EnemyCount = 1, RubblePos = rpend };
            left = 1;
        }
        if (left == 0)
        {
            Phase = MacroPhase.Navigating;
            // Area clear: pick up nearby coins/health first (just walk over them), then move on to the next area.
            if (S.Combat.AutoArise && Arise(s, now, S) is { } clearRise) return clearRise;
            if (Pickup(s, now, PickupClearRadius, healthOnly: false, S) is { } pick) return pick;
            if (s.Lift is { } lift) return TrialsLift(s, lift, now, S);
            if (!S.Navigation.AutoNavigate) { Status = $"{area} clear (auto navigation off)."; return []; }
            if (s.NextAreaPos is null && s.CurrentArea is { } cur && s.PlayerPos is { } pIn && !cur.Contains(pIn, 10))
            {
                var walkIn = WalkPath(s, cur.Center, S.Navigation.ArriveRadius, now, "combat area", S.Navigation.AttackWhenStuck);
                Status = "Walking into the combat area.";
                return walkIn.Count > 0 ? walkIn : [new MoveAction([], "Reached area")];
            }
            if (EnterTarget(s, S, now) is not { } next)
            {
                Status = $"{area} clear, next area unreadable.";
                return [new MoveAction([], "No destination"), .. Note("nav-none", now, 6000, $"{area} clear but the next area position (World.Spawns/Areas) is unreadable. HUD text: '{s.HudRaw ?? "(none)"}'{(s.RunWave is { Length: > 0 } rw ? $", run wave '{rw}'" : "")}.")];
            }
            var walk = WalkPath(s, next, S.Navigation.ArriveRadius, now, $"area {hud!.TargetArea}", S.Navigation.AttackWhenStuck);
            Status = walk.Count == 0 ? $"Reached area {hud!.TargetArea}, waiting for enemies." : $"{(hud!.Enter ? $"Enter area {hud.TargetArea}/{hud.AreaTotal}" : $"{area} clear")} → walking to area {hud.TargetArea}.";
            return walk.Count == 0 ? [new MoveAction([], "Arrived")] : walk;
        }
        Phase = MacroPhase.Fighting;
        Status = $"{Wave(s)}{area} · {(left < 0 ? "?" : left.ToString())} enemies";
        var actions = new List<MacroAction>();
        // Tracking: nearest living enemy → walk closer until within attack range, cursor aimed at the enemy.
        // While approaching only Dash (Q) is used; basic attack + skills 1/2/3 only once within attack range.
        bool approaching = false;
        EnemyInfo? tgt = null;
        var candidates = Targets(s);
        // Low HP while fighting: grab the nearest health first.
        if (s.PlayerHp is { } hp && hp < LowHpPickup && Pickup(s, now, PickupHealthRadius, healthOnly: true, S) is { } heal)
        {
            Status = $"{Status} · HP {hp:P0} → grab health";
            return heal;
        }
        // v0.2.67 Shadow Lord: corpse with an ARISE prompt (≤ 40 studs) → approach, hold E (HoldDuration + 0.25 s), keep fighting.
        if (S.Combat.AutoArise && Arise(s, now, S) is { } rise) { Status = $"{Wave(s)}{Status}"; return rise; }
        // Nearby items (≤ 30 studs) are approached while still attacking: enemies come to the player anyway. Failing for 6 s → skipped for 60 s.
        string fightStatus = Status;
        var lootWalk = candidates.Count > 0 ? Pickup(s, now, PickupNearRadius, healthOnly: false, S) : null;
        string? lootStatus = lootWalk is not null ? Status : null;
        Status = fightStatus;
        // No targetable enemy yet (summons not spawned, boss IMMUNE): pick up items while waiting.
        if (candidates.Count == 0 && Pickup(s, now, PickupClearRadius, healthOnly: false, S) is { } idlePick) return idlePick;
        if (s.PlayerPos is { } meIn && candidates.Count == 0)
        {
            // Only the IMMUNE boss is left / enemies not spawned yet: move into the area (e.g. climb the stairs from the raid spawn).
            if (s.CurrentArea is { } box && !box.Contains(meIn, 10))
            {
                Phase = MacroPhase.Navigating;
                var walkIn = WalkPath(s, box.Center, S.Navigation.ArriveRadius, now, "combat area", S.Navigation.AttackWhenStuck);
                Status = s.BossImmune ? $"Boss {s.BossName} IMMUNE → entering the area first." : "Walking into the combat area.";
                return walkIn.Count > 0 ? walkIn : [new MoveAction([], "Reached area")];
            }
            if (s.BossImmune)
            {
                Status = $"{Wave(s)}Boss {s.BossName} IMMUNE, waiting for summons.";
                return Ready("jump-wait", now, 1100) ? [new MoveAction([], "Wait for summons"), new KeyAction(VirtualKeys.Space, 80, "Jump (waiting for summons)")] : [new MoveAction([], "Wait for summons")];
            }
        }
        if (s.PlayerPos is { } me && candidates
                .OrderBy(e => Navigator.Distance2D(me.X, me.Z, e.Pos.X, e.Pos.Z)).FirstOrDefault() is { } target)
        {
            tgt = target;
            double d = Navigator.Distance2D(me.X, me.Z, target.Pos.X, target.Pos.Z);
            Status += $" · target #{target.Name} {d:0} studs";
            if (S.Navigation.ChaseEnemies)
            {
                // Attack range is maintained: farther than range + tolerance → chase; inside the band → roam on a circle with radius = attack range
                // (too close backs off automatically because the roaming point lies on that circle).
                bool isRubble = target.Name == AnimeZeroPaths.RubbleName;
                // Rubble = solid wall: get close enough (≤ 12 studs), then attack; walk straight without A* (the point is inside the wall).
                double range = isRubble ? Math.Min(12, S.Navigation.AttackRange) : S.Navigation.AttackRange;
                // v0.2.91: ClimbStop in front of the rubble is readable → goal = nearest ClimbStop point (the reachable limit), until ≤ 3 studs.
                var goal = target.Pos;
                double dGoal = d;
                if (isRubble && s.RubbleStand is { } stand)
                {
                    goal = stand; range = 3;
                    dGoal = Navigator.Distance2D(me.X, me.Z, stand.X, stand.Z);
                }
                approaching = lootWalk is null && dGoal > range + (isRubble ? 2 : RangeTolerance(S.Navigation.AttackRange));
                // v0.3.31: pressed against the rubble debris (stuck ≥ 1.5 s, ≤ 60 studs) = as close as it gets → attack with everything.
                if (isRubble && approaching && dGoal <= RubbleStraightRange && _stuckRef is { } sr && now - _stuckSince >= 1500
                    && Navigator.Distance2D(me.X, me.Z, sr.X, sr.Z) <= 1.5)
                {
                    approaching = false;
                    Status += " · against the debris";
                }
                if (isRubble) Status += $" · rubble {target.Health:P0}";
                if (lootWalk is not null) { actions.AddRange(lootWalk); Status += $" · {lootStatus}"; }
                var walk = lootWalk is not null ? [] : approaching ? WalkPath(s, goal, range, now, isRubble ? "rubble" : $"enemy #{target.Name}", isRubble) : [];
                if (lootWalk is null) actions.AddRange(walk.Count > 0 ? walk : [new MoveAction([], "Near enemy")]);
            }
            // Aim at the group center: average position of living enemies ≤ 15 studs from the target (most skills are AOE).
            var group = candidates.Where(e => Navigator.Distance2D(e.Pos.X, e.Pos.Z, target.Pos.X, target.Pos.Z) <= ClusterRadius).ToList();
            var aimPos = group.Count > 1
                ? new Vec3(group.Average(e => e.Pos.X), group.Average(e => e.Pos.Y), group.Average(e => e.Pos.Z))
                : target.Pos;
            // v0.2.59: when surrounded, the group center lands near/under the player → camera looks down at the feet & attacks miss.
            // Use the group center only when ≥ 8 studs from the player and in the target's direction (≤ 40°); otherwise aim at the nearest target.
            if (group.Count > 1 && s.PlayerPos is { } mpos)
            {
                double gx = aimPos.X - mpos.X, gz = aimPos.Z - mpos.Z, tx = target.Pos.X - mpos.X, tz = target.Pos.Z - mpos.Z;
                double gl = Math.Sqrt(gx * gx + gz * gz), tl = Math.Sqrt(tx * tx + tz * tz);
                bool sameSide = gl >= 8 && tl > 0.1 && (gx * tx + gz * tz) / (gl * tl) >= Math.Cos(40 * Math.PI / 180);
                if (!sameSide) { aimPos = target.Pos; group = [target]; }
            }
            // Enemy on the same surface (player not airborne): aim at the player's body height, do not look down.
            if (s.PlayerPos is { } me2 && me2.Y - aimPos.Y < 6) aimPos = aimPos with { Y = Math.Max(aimPos.Y, me2.Y - 1) };
            string aimLabel = group.Count > 1 ? $"center of {group.Count} enemies" : $"enemy #{target.Name}";
            if (group.Count > 1) Status += $" · aiming at {group.Count} enemies";
            if (S.Navigation.AimAtEnemy && s.ShiftLock)
            {
                // Shift lock: cursor locked at the center → turn the camera (yaw) toward the enemy with relative movement.
                // Yaw and pitch are computed from the camera position (shift lock shifts the camera to the right shoulder); pitch kept within -80°..+10°.
                int dx = 0, dy = 0;
                if (s.Camera is { } cam)
                {
                    dx = Turn(cam.Look.X, cam.Look.Z, aimPos.X - cam.Pos.X, aimPos.Z - cam.Pos.Z, S.Navigation.TurnPixelsPerDegree);
                    dy = Pitch(cam.Look, aimPos.X - cam.Pos.X, aimPos.Y - cam.Pos.Y, aimPos.Z - cam.Pos.Z, S.Navigation.TurnPixelsPerDegree);
                }
                else if (s.CameraLook is { } cl)
                    dx = Turn(cl.X, cl.Z, aimPos.X - me.X, aimPos.Z - me.Z, S.Navigation.TurnPixelsPerDegree);
                if (dx != 0 || dy != 0)
                    actions.Add(new TurnAction(dx, dy, $"Turn camera to {aimLabel}"));
            }
            else if (S.Navigation.AimAtEnemy && s.Camera is { } cam2 && Ready("cam-drag", now, 700))
            {
                // Without shift lock: skills follow the cursor, so the camera is turned too (hold right click + drag)
                // when the enemy is off-center by > 20° or pitch leaves the -80°..+10° range.
                double ex = aimPos.X - cam2.Pos.X, ey = aimPos.Y - cam2.Pos.Y, ez = aimPos.Z - cam2.Pos.Z;
                int tdx = Turn(cam2.Look.X, cam2.Look.Z, ex, ez, S.Navigation.TurnPixelsPerDegree, gain: 1, deadzone: 20, max: 200);
                int tdy = Pitch(cam2.Look, ex, ey, ez, S.Navigation.TurnPixelsPerDegree, gain: 1, deadzone: 8, max: 150);
                if (tdx != 0 || tdy != 0)
                    actions.Add(new TurnAction(tdx, tdy, $"Turn camera (right click) to {aimLabel}", Drag: true));
                else _cooldowns.Remove("cam-drag");
            }
            // v0.2.79: projection is only used when the camera ViewportSize = window client area (±2 px); a mismatch (DPI/scaling) = direction uncertain.
            if (S.Navigation.AimAtEnemy && !s.ShiftLock && s.ViewportMatchesClient && s.Camera?.WorldToScreen(aimPos) is { } sp
                && sp.X >= 2 && sp.Y >= 2 && sp.X <= s.ClientW - 2 && sp.Y <= s.ClientH - 2)
                actions.Add(new AimAction(sp.X, sp.Y, $"Aim at {aimLabel}"));
        }

        // No target (enemies count > 0 but the model has no HumanoidRootPart/HP yet): do not attack thin air.
        // HUD "Enter area N" → walk to that area; otherwise wait.
        if (tgt is null && hud is { Enter: true } && EnterTarget(s, S, now) is { } enterPos && S.Navigation.AutoNavigate)
        {
            Phase = MacroPhase.Navigating;
            var go = WalkPath(s, enterPos, S.Navigation.ArriveRadius, now, $"area {hud.TargetArea}", S.Navigation.AttackWhenStuck);
            Status = $"Entering area {hud.TargetArea}/{hud.AreaTotal} (enemies not read yet).";
            return go.Count > 0 ? go : [new MoveAction([], "Arrived")];
        }
        var skills = S.Combat.EffectiveSkills.Where(k => k.Enabled && HotkeyMap.TryGetVirtualKey(k.Key, out _)).ToList();
        var dash = skills.FirstOrDefault(IsDash);
        var swap = skills.FirstOrDefault(IsSwap);
        if (approaching)
        {
            // Dash only when still far enough (> 2× attack range) and not on cooldown (HUD Dash.Progress).
            if (dash is not null && s.DashCooling == false && Ready("dash", now, Math.Max(800, dash.CooldownMs))
                && s.PlayerPos is { } p0 && actions.OfType<MoveAction>().Any(m => m.Keys.Count > 0)
                && s.Enemies.Any(e => e.Alive && Navigator.Distance2D(p0.X, p0.Z, e.Pos.X, e.Pos.Z) > 2 * S.Navigation.AttackRange)
                && HotkeyMap.TryGetVirtualKey(dash.Key, out uint dvk))
                actions.Add(new KeyAction((ushort)dvk, 40, $"{dash.Name} ({dash.Key}) approach"));
            return actions;
        }

        // Skills 1/2/3: one per tick, the least recently used one that is ready. When the HUD is readable, cooldown/lock comes from the HUD
        // (minimum 400 ms gap so the HUD can update). v0.2.79 (no guessing): a skill missing from the HUD / with unreadable cd text
        // is NOT pressed (previously used CooldownMs from settings).
        // v0.2.65: Ultimate (G, "use when the ult bar is full") is not counted with skills 1/2/3 / power swap.
        var ult = skills.FirstOrDefault(IsUlt);
        var cds = s.SkillCooldowns.Where(c => ult is null || !c.Key.Equals(ult.Key, StringComparison.OrdinalIgnoreCase)).ToList();
        var hudCd = s.SkillCooldowns.GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        bool SkillReady(SkillBinding k) => hudCd.TryGetValue(k.Key, out var c)
            && c.Ready && (!_skillLast.TryGetValue(k.Name, out long t0) || now - t0 >= 400);
        var main = skills.Where(k => !IsDash(k) && !IsSwap(k) && !IsUlt(k)).ToList();
        // Ultimate: press G periodically while fighting (the game rejects it while the ult bar is not full); HUD is used when a G box exists.
        // v0.3.21: boss-only Ultimate (setting UltBossOnly) → only when BossBar is shown and not [IMMUNE]; otherwise G is never pressed.
        bool ultBossOk = !S.Combat.UltBossOnly || (s.BossBarVisible && !s.BossImmune);
        bool ultNow = ult is not null && ultBossOk && now - _lastSkillPress >= 300
            && (s.UltReady ?? (hudCd.TryGetValue(ult.Key, out var uc) && uc.Ready))
            && (!_skillLast.TryGetValue(ult.Name, out long ut) || now - ut >= Math.Max(800, ult.CooldownMs))
            && HotkeyMap.TryGetVirtualKey(ult.Key, out _);
        var mainDue = main.Where(SkillReady)
            .OrderBy(k => _skillLast.TryGetValue(k.Name, out long t) ? t : long.MinValue)
            .FirstOrDefault();
        // Skills 1/2/3 take priority; G in between.
        // All skills on cooldown + power swap enabled → T takes priority (G must not swallow the T animation).
        // HUD ult reads ready → G takes priority; unreadable → G only between skills and not while waiting for T.
        if (s.UltReady != true && (mainDue is not null || (swap is not null && cds.Count > 0 && cds.All(c => c.Cooling)))) ultNow = false;
        var due = ultNow ? ult : mainDue;
        // Basic attack (continuous clicking) can swallow skill/T keys in game, so when a skill or T is pressed,
        // basic attack is skipped that tick and held off briefly afterwards.
        if (due is not null && HotkeyMap.TryGetVirtualKey(due.Key, out uint vk))
        {
            _skillLast[due.Name] = now;
            _lastSkillPress = now;
            _attackHoldUntil = now + 250;
            // While a skill animation plays the game rejects Space → jump first (while roaming), then release the skill in the air.
            if (!ultNow && !approaching && S.Navigation.ChaseEnemies && now - _lastJump >= 600)
            {
                _lastJump = now;
                _nextRoamJump = now + 700 + _rng.Next(600);
                actions.Add(new KeyAction(VirtualKeys.Space, 90, "Jump (before skill)"));
            }
            actions.Add(new KeyAction((ushort)vk, 60, $"{due.Name} ({due.Key})"));
        }
        // Power swap (T) only when skills 1, 2, 3 on the HUD are all on cooldown/locked. The game rejects T while the attack
        // animation is still playing, so: stop basic attack first (keep moving), wait for the animation to end, then press T.
        // If the HUD still shows "all on cooldown" 1.2 s after T, retry (max 3×), then pause 4 s.
        else if (swap is not null && cds.Count > 0 && cds.All(c => c.Cooling)
                 && HotkeyMap.TryGetVirtualKey(swap.Key, out uint svk) && now >= _swapRestUntil)
        {
            if (_swapPrepSince is not long since)
            {
                _swapPrepSince = since = now;
                actions.Add(new NoteAction($"All skills on cooldown → stop attacking, ready for {swap.Name} ({swap.Key})."));
            }
            _attackHoldUntil = now + 200;
            if (now - since >= SwapPrepMs && now - _swapLastPress >= 1200)
            {
                if (_swapTries >= 3) { _swapTries = 0; _swapPrepSince = null; _swapRestUntil = now + 4000; }
                else
                {
                    _swapTries++;
                    _swapLastPress = now;
                    actions.Add(new KeyAction((ushort)svk, 120, $"{swap.Name} ({swap.Key}): all skills on cooldown (try {_swapTries})"));
                }
            }
        }
        else if (ResetSwap() && now >= _attackHoldUntil && now - _lastAttack >= Math.Max(30, S.Combat.AttackIntervalMs))
        {
            _lastAttack = now;
            actions.Add(new AttackAction("Basic attack"));
        }
        if (swap is null && cds.Count > 0 && cds.All(c => c.Cooling)
            && S.Combat.EffectiveSkills.FirstOrDefault(IsSwap) is { Enabled: false } off && Ready("swap-off", now, 15000))
            actions.Add(new NoteAction($"All skills on cooldown, but {off.Name} ({off.Key}) is disabled in the Combat card → enable it so the power swaps."));
        // Already close: keep circling the enemy + jumping (both when using skills and with basic attack only).
        Roam(s, S, tgt, now, actions);
        // Skills 1/2/3 used up (all on cooldown / none ready): Dash (Q) in the roaming direction so we are not a standing target.
        bool skillsOut = cds.Count > 0 && cds.All(c => c.Cooling);
        // Do not Dash ≤ 500 ms before T is pressed (the Dash animation can swallow T) or in the same tick.
        bool swapSoon = _swapPrepSince is long ps && now >= _swapRestUntil && Math.Max(ps + SwapPrepMs, _swapLastPress + 1200) - now < 500;
        bool swapNow = swap is not null && actions.Any(a => a is KeyAction k && k.Reason.StartsWith(swap.Name, StringComparison.Ordinal));
        // Also between skills: after the skill animation ends and no skill is pressed this tick → Dash sideways (roaming direction).
        bool skillGap = (due is null || ultNow) && now - _lastSkillPress >= SkillAnimMs && now - _lastJump >= 250;
        if ((skillsOut || skillGap) && dash is not null && s.DashCooling == false && !swapSoon && !swapNow
            && actions.OfType<MoveAction>().Any(m => m.Keys.Count > 0) && Ready("dash-roam", now, Math.Max(1500, dash.CooldownMs))
            && HotkeyMap.TryGetVirtualKey(dash.Key, out uint rvk))
            actions.Add(new KeyAction((ushort)rvk, 40, $"{dash.Name} ({dash.Key}) evade{(skillsOut ? ", skills used up" : " between skills")}"));
        return actions;
    }

    /// <summary>
    /// Already within attack range: walk in a circle around the enemy (radius ~0.7× attack range, ±80°/s)
    /// jumping every 0.9–1.6 s instead of standing still.
    /// </summary>
    private void Roam(GameSnapshot s, AppSettings S, EnemyInfo? target, long now, List<MacroAction> actions)
    {
        if (!S.Navigation.RoamWhenSkillsCooling || !S.Navigation.ChaseEnemies || target is null
            || s.PlayerPos is not { } p || s.CameraLook is not { } look) return;
        double r = Math.Max(2.5, S.Navigation.AttackRange);
        if (_roamSince is not long t0 || now - _roamLast > 1500) { t0 = now; _roamSince = now; _roamAngle = Math.Atan2(p.Z - target.Pos.Z, p.X - target.Pos.X); }
        _roamLast = now;
        // Clockwise/counter-clockwise direction switches randomly every 2.5–4.5 s.
        if (now >= _roamFlipAt)
        {
            if (_roamFlipAt != 0) { _roamAngle += _roamDir * (now - t0) / 1000.0 * Math.Min(80 * Math.PI / 180, 12.0 / r); _roamSince = t0 = now; _roamDir = -_roamDir; }
            _roamFlipAt = now + 2500 + _rng.Next(2000);
        }
        // Angular speed is reduced for large circles (± 12 studs/s along the circumference); target point 25° ahead so the path stays close to the circle.
        double omega = Math.Min(80 * Math.PI / 180, 12.0 / r);
        double a = _roamAngle + _roamDir * (Math.PI * 25 / 180 + (now - t0) / 1000.0 * omega);
        // Radial correction: too close → target point shifted outward, too far → inward, so the distance returns to r.
        double cur = Navigator.Distance2D(p.X, p.Z, target.Pos.X, target.Pos.Z);
        r = Math.Max(2.5, r + Math.Clamp(r - cur, -r * 0.5, r * 0.5));
        double gx = target.Pos.X + r * Math.Cos(a), gz = target.Pos.Z + r * Math.Sin(a);
        var keys = Navigator.KeysToward(p.X, p.Z, look.X, look.Z, gx, gz, 0.8);
        actions.RemoveAll(x => x is MoveAction);
        actions.Insert(0, new MoveAction(keys, "Roaming around enemy"));
        // The scheduled jump is postponed when a skill was just pressed (< 700 ms, the skill animation swallows Space) or we already jumped before the skill.
        if (now >= _nextRoamJump && now - _lastSkillPress >= SkillAnimMs && now - _lastJump >= 600)
        {
            _nextRoamJump = now + 700 + _rng.Next(600);
            _lastJump = now;
            // The game rejects jumps during the attack animation: drop the attack click this tick and hold attacks for 300 ms.
            actions.RemoveAll(x => x is AttackAction);
            _attackHoldUntil = Math.Max(_attackHoldUntil, now + 300);
            actions.Add(new KeyAction(VirtualKeys.Space, 100, "Jump (roaming)"));
        }
    }
    /// <summary>Attack range tolerance band (studs): max(2, 10%).</summary>
    public static double RangeTolerance(double range) => Math.Max(2, range * 0.1);
    private long _attackHoldUntil, _swapLastPress = long.MinValue / 2, _swapRestUntil;
    private long? _swapPrepSince;
    private int _swapTries;
    /// <summary>How long basic attack stops before T so the attack animation finishes.</summary>
    private const int SwapPrepMs = 900;
    private bool ResetSwap() { _swapPrepSince = null; _swapTries = 0; return true; }
    private long? _roamSince;
    private long _roamLast, _nextRoamJump, _roamFlipAt, _lastJump = long.MinValue / 2, _lastSkillPress = long.MinValue / 2;
    /// <summary>Estimated skill animation length (the game rejects Space/Dash during it).</summary>
    private const int SkillAnimMs = 700;
    private int _roamDir = 1;
    private double _roamAngle;
    private readonly Random _rng = new();

    /// <summary>Walk to the destination with W/A/S/D; stuck ≥1.5 s → jump (+ attack when allowed, for rubble).</summary>
    // ---------- Items on the floor (Workspace.PooledDrops) ----------
}

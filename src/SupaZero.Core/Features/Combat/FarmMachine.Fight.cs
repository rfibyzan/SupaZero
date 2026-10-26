using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    private List<MacroAction> Fight(GameSnapshot s, AppSettings S, long now)
    {
        var hud = s.Hud;
        int left = s.EnemyCount >= 0 ? s.EnemyCount : hud?.EnemiesLeft ?? -1;
        string area = hud is { AreaTotal: > 0 } ? $"Area {hud.Area}/{hud.AreaTotal}" : "Area ?";
        if (s.HudRaw?.Contains("rubble", StringComparison.OrdinalIgnoreCase) == true)
            _pending.AddRange(Note("rubble-diag", now, 8000, s.RubblePos is { } rp
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Rubble di ({rp.X:0},{rp.Y:0},{rp.Z:0}){(s.RubbleStand is { } rs ? $", berdiri ({rs.X:0},{rs.Z:0})" : "")} [{s.RubbleInfo}], pemain {(s.PlayerPos is { } pp ? $"({pp.X:0},{pp.Z:0})" : "?")}.")
                : $"Rubble aktif tapi posisinya tidak terbaca: {s.RubbleInfo}."));
        // v0.3.12 (Byzan 26 Okt): rubble Hxh ada sejak awal; setelah wave area ini bersih rubble bisa dipukul walau HUD belum
        // menyebut "Break the rubble". Rubble = Adornee RubbleBar dengan Blocker masih padat (bukti v0.2.91), dekat area ini.
        bool waveAlive = s.Enemies.Any(e => e.Alive && e.Name != AnimeZeroPaths.RubbleName);
        if (waveAlive && hud is { Area: > 0, Enter: false }) _waveArea = hud.Area;
        if (left == 0 && s.RubblePending is { } rpend && hud is { Area: > 0 } && _waveArea > 0
            && (hud.Area == _waveArea || (hud.Enter && hud.Area == _waveArea + 1)) && RubbleNear(s, rpend))
        {
            _pending.AddRange(Note("rubble-pending", now, 10000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Wave area {_waveArea} bersih → serang rubble di ({rpend.X:0},{rpend.Z:0}) [{s.RubbleInfo}].")));
            s = s with { Enemies = [new EnemyInfo(AnimeZeroPaths.RubbleName, rpend, Math.Max(0.01, s.RubbleHp ?? 1), 1)], EnemyCount = 1, RubblePos = rpend };
            left = 1;
        }
        if (left == 0)
        {
            Phase = MacroPhase.Navigating;
            // Area bersih: pungut koin/health di sekitar dulu (cukup dilewati), baru lanjut ke area berikutnya.
            if (S.Combat.AutoArise && Arise(s, now, S) is { } clearRise) return clearRise;
            if (Pickup(s, now, PickupClearRadius, healthOnly: false, S) is { } pick) return pick;
            if (!S.Navigation.AutoNavigate) { Status = $"{area} bersih (Auto navigasi mati)."; return []; }
            if (s.NextAreaPos is null && s.CurrentArea is { } cur && s.PlayerPos is { } pIn && !cur.Contains(pIn, 10))
            {
                var walkIn = WalkPath(s, cur.Center, S.Navigation.ArriveRadius, now, "area pertarungan", S.Navigation.AttackWhenStuck);
                Status = "Jalan masuk area pertarungan.";
                return walkIn.Count > 0 ? walkIn : [new MoveAction([], "Sampai area")];
            }
            if (EnterTarget(s, S, now) is not { } next)
            {
                Status = $"{area} bersih, area berikutnya tidak terbaca.";
                return [new MoveAction([], "Tujuan tidak ada"), .. Note("nav-none", now, 6000, $"{area} bersih tapi posisi area berikutnya (World.Spawns/Areas) tidak terbaca.")];
            }
            var walk = WalkPath(s, next, S.Navigation.ArriveRadius, now, $"area {hud!.TargetArea}", S.Navigation.AttackWhenStuck);
            Status = walk.Count == 0 ? $"Sampai area {hud!.TargetArea}, menunggu musuh." : $"{(hud!.Enter ? $"Masuk area {hud.TargetArea}/{hud.AreaTotal}" : $"{area} bersih")} → jalan ke area {hud.TargetArea}.";
            return walk.Count == 0 ? [new MoveAction([], "Sampai")] : walk;
        }
        Phase = MacroPhase.Fighting;
        Status = $"{Wave(s)}{area} · {(left < 0 ? "?" : left.ToString())} musuh";
        var actions = new List<MacroAction>();
        // Tracking: musuh hidup terdekat → jalan mendekat sampai jarak serang, kursor diarahkan ke musuh.
        // Selama mendekat hanya Dash (Q) yang dipakai; serangan dasar + skill 1/2/3 baru setelah dalam jarak serang.
        bool approaching = false;
        EnemyInfo? tgt = null;
        var candidates = Targets(s);
        // Darah rendah saat bertarung: ambil health terdekat dulu.
        if (s.PlayerHp is { } hp && hp < LowHpPickup && Pickup(s, now, PickupHealthRadius, healthOnly: true, S) is { } heal)
        {
            Status = $"{Status} · darah {hp:P0} → ambil health";
            return heal;
        }
        // v0.2.67 Shadow Lord: mayat berprompt ARISE (≤ 40 stud) → dekati, tahan E (HoldDuration + 0,25 dtk), lanjut bertarung.
        if (S.Combat.AutoArise && Arise(s, now, S) is { } rise) { Status = $"{Wave(s)}{Status}"; return rise; }
        // Item dekat (≤ 30 stud) didekati sambil tetap menyerang: musuh toh menghampiri pemain. Gagal 6 dtk → dilewati 60 dtk.
        string fightStatus = Status;
        var lootWalk = candidates.Count > 0 ? Pickup(s, now, PickupNearRadius, healthOnly: false, S) : null;
        string? lootStatus = lootWalk is not null ? Status : null;
        Status = fightStatus;
        // Belum ada musuh yang bisa diincar (summon belum muncul, boss IMMUNE): pungut item sambil menunggu.
        if (candidates.Count == 0 && Pickup(s, now, PickupClearRadius, healthOnly: false, S) is { } idlePick) return idlePick;
        if (s.PlayerPos is { } meIn && candidates.Count == 0)
        {
            // Hanya boss IMMUNE yang tersisa / musuh belum muncul: masuk ke area (mis. naik tangga dari spawn raid).
            if (s.CurrentArea is { } box && !box.Contains(meIn, 10))
            {
                Phase = MacroPhase.Navigating;
                var walkIn = WalkPath(s, box.Center, S.Navigation.ArriveRadius, now, "area pertarungan", S.Navigation.AttackWhenStuck);
                Status = s.BossImmune ? $"Boss {s.BossName} IMMUNE → masuk area dulu." : "Jalan masuk area pertarungan.";
                return walkIn.Count > 0 ? walkIn : [new MoveAction([], "Sampai area")];
            }
            if (s.BossImmune)
            {
                Status = $"{Wave(s)}Boss {s.BossName} IMMUNE, menunggu summon.";
                return Ready("jump-wait", now, 1100) ? [new MoveAction([], "Tunggu summon"), new KeyAction(VirtualKeys.Space, 80, "Lompat (menunggu summon)")] : [new MoveAction([], "Tunggu summon")];
            }
        }
        if (s.PlayerPos is { } me && candidates
                .OrderBy(e => Navigator.Distance2D(me.X, me.Z, e.Pos.X, e.Pos.Z)).FirstOrDefault() is { } target)
        {
            tgt = target;
            double d = Navigator.Distance2D(me.X, me.Z, target.Pos.X, target.Pos.Z);
            Status += $" · target #{target.Name} {d:0} stud";
            if (S.Navigation.ChaseEnemies)
            {
                // Jarak serang dipertahankan: lebih jauh dari jarak + toleransi → kejar; dalam pita → roaming di lingkaran berjari-jari jarak serang
                // (terlalu dekat otomatis mundur karena titik roaming ada di lingkaran itu).
                bool isRubble = target.Name == AnimeZeroPaths.RubbleName;
                // Rubble = dinding solid: cukup dekat (≤ 12 stud) lalu serang; jalan lurus tanpa A* (titiknya di dalam dinding).
                double range = isRubble ? Math.Min(12, S.Navigation.AttackRange) : S.Navigation.AttackRange;
                // v0.2.91: ClimbStop di depan rubble terbaca → tujuan = titik ClimbStop terdekat (batas yang bisa dicapai), sampai ≤ 3 stud.
                var goal = target.Pos;
                double dGoal = d;
                if (isRubble && s.RubbleStand is { } stand)
                {
                    goal = stand; range = 3;
                    dGoal = Navigator.Distance2D(me.X, me.Z, stand.X, stand.Z);
                }
                approaching = lootWalk is null && dGoal > range + (isRubble ? 2 : RangeTolerance(S.Navigation.AttackRange));
                if (isRubble) Status += $" · rubble {target.Health:P0}";
                if (lootWalk is not null) { actions.AddRange(lootWalk); Status += $" · {lootStatus}"; }
                var walk = lootWalk is not null ? [] : approaching ? WalkPath(s, goal, range, now, isRubble ? "rubble" : $"musuh #{target.Name}", isRubble) : [];
                if (lootWalk is null) actions.AddRange(walk.Count > 0 ? walk : [new MoveAction([], "Dekat musuh")]);
            }
            // Bidik ke tengah kelompok: rata-rata posisi musuh hidup ≤ 15 stud dari target (skill kebanyakan AOE).
            var group = candidates.Where(e => Navigator.Distance2D(e.Pos.X, e.Pos.Z, target.Pos.X, target.Pos.Z) <= ClusterRadius).ToList();
            var aimPos = group.Count > 1
                ? new Vec3(group.Average(e => e.Pos.X), group.Average(e => e.Pos.Y), group.Average(e => e.Pos.Z))
                : target.Pos;
            // v0.2.59: saat dikepung, tengah kelompok jatuh di dekat/di bawah pemain → kamera menunduk ke kaki & serangan meleset.
            // Pakai tengah kelompok hanya bila ≥ 8 stud dari pemain dan searah target (≤ 40°); selain itu bidik target terdekat.
            if (group.Count > 1 && s.PlayerPos is { } mpos)
            {
                double gx = aimPos.X - mpos.X, gz = aimPos.Z - mpos.Z, tx = target.Pos.X - mpos.X, tz = target.Pos.Z - mpos.Z;
                double gl = Math.Sqrt(gx * gx + gz * gz), tl = Math.Sqrt(tx * tx + tz * tz);
                bool sameSide = gl >= 8 && tl > 0.1 && (gx * tx + gz * tz) / (gl * tl) >= Math.Cos(40 * Math.PI / 180);
                if (!sameSide) { aimPos = target.Pos; group = [target]; }
            }
            // Musuh di permukaan yang sama (bukan pemain di udara): bidik setinggi badan pemain, jangan menunduk.
            if (s.PlayerPos is { } me2 && me2.Y - aimPos.Y < 6) aimPos = aimPos with { Y = Math.Max(aimPos.Y, me2.Y - 1) };
            string aimLabel = group.Count > 1 ? $"tengah {group.Count} musuh" : $"musuh #{target.Name}";
            if (group.Count > 1) Status += $" · bidik {group.Count} musuh";
            if (S.Navigation.AimAtEnemy && s.ShiftLock)
            {
                // Shift lock: kursor terkunci di tengah → putar kamera (yaw) ke arah musuh dengan gerak relatif.
                // Yaw dan pitch dihitung dari posisi kamera (shift lock menggeser kamera ke kanan bahu); pitch dijaga -80°..+10°.
                int dx = 0, dy = 0;
                if (s.Camera is { } cam)
                {
                    dx = Turn(cam.Look.X, cam.Look.Z, aimPos.X - cam.Pos.X, aimPos.Z - cam.Pos.Z, S.Navigation.TurnPixelsPerDegree);
                    dy = Pitch(cam.Look, aimPos.X - cam.Pos.X, aimPos.Y - cam.Pos.Y, aimPos.Z - cam.Pos.Z, S.Navigation.TurnPixelsPerDegree);
                }
                else if (s.CameraLook is { } cl)
                    dx = Turn(cl.X, cl.Z, aimPos.X - me.X, aimPos.Z - me.Z, S.Navigation.TurnPixelsPerDegree);
                if (dx != 0 || dy != 0)
                    actions.Add(new TurnAction(dx, dy, $"Putar kamera ke {aimLabel}"));
            }
            else if (S.Navigation.AimAtEnemy && s.Camera is { } cam2 && Ready("cam-drag", now, 700))
            {
                // Tanpa shift lock: skill mengikuti kursor, jadi kamera ikut diputar (klik kanan tahan + geser)
                // bila musuh melenceng > 20° dari tengah atau pitch keluar batas -80°..+10°.
                double ex = aimPos.X - cam2.Pos.X, ey = aimPos.Y - cam2.Pos.Y, ez = aimPos.Z - cam2.Pos.Z;
                int tdx = Turn(cam2.Look.X, cam2.Look.Z, ex, ez, S.Navigation.TurnPixelsPerDegree, gain: 1, deadzone: 20, max: 200);
                int tdy = Pitch(cam2.Look, ex, ey, ez, S.Navigation.TurnPixelsPerDegree, gain: 1, deadzone: 8, max: 150);
                if (tdx != 0 || tdy != 0)
                    actions.Add(new TurnAction(tdx, tdy, $"Putar kamera (klik kanan) ke {aimLabel}", Drag: true));
                else _cooldowns.Remove("cam-drag");
            }
            // v0.2.79: proyeksi hanya dipakai bila ViewportSize kamera = area klien jendela (±2 px); beda (DPI/skala) = arah tidak pasti.
            if (S.Navigation.AimAtEnemy && !s.ShiftLock && s.ViewportMatchesClient && s.Camera?.WorldToScreen(aimPos) is { } sp
                && sp.X >= 2 && sp.Y >= 2 && sp.X <= s.ClientW - 2 && sp.Y <= s.ClientH - 2)
                actions.Add(new AimAction(sp.X, sp.Y, $"Arahkan ke {aimLabel}"));
        }

        // Tidak ada target (hitungan enemies > 0 tapi model belum punya HumanoidRootPart/HP): jangan serang udara.
        // HUD "Enter area N" → jalan ke area itu; selain itu tunggu.
        if (tgt is null && hud is { Enter: true } && EnterTarget(s, S, now) is { } enterPos && S.Navigation.AutoNavigate)
        {
            Phase = MacroPhase.Navigating;
            var go = WalkPath(s, enterPos, S.Navigation.ArriveRadius, now, $"area {hud.TargetArea}", S.Navigation.AttackWhenStuck);
            Status = $"Masuk area {hud.TargetArea}/{hud.AreaTotal} (musuh belum terbaca).";
            return go.Count > 0 ? go : [new MoveAction([], "Sampai")];
        }
        var skills = S.Combat.EffectiveSkills.Where(k => k.Enabled && HotkeyMap.TryGetVirtualKey(k.Key, out _)).ToList();
        var dash = skills.FirstOrDefault(IsDash);
        var swap = skills.FirstOrDefault(IsSwap);
        if (approaching)
        {
            // Dash hanya bila masih cukup jauh (> 2× jarak serang) dan tidak sedang cooldown (HUD Dash.Progress).
            if (dash is not null && s.DashCooling == false && Ready("dash", now, Math.Max(800, dash.CooldownMs))
                && s.PlayerPos is { } p0 && actions.OfType<MoveAction>().Any(m => m.Keys.Count > 0)
                && s.Enemies.Any(e => e.Alive && Navigator.Distance2D(p0.X, p0.Z, e.Pos.X, e.Pos.Z) > 2 * S.Navigation.AttackRange)
                && HotkeyMap.TryGetVirtualKey(dash.Key, out uint dvk))
                actions.Add(new KeyAction((ushort)dvk, 40, $"{dash.Name} ({dash.Key}) mendekat"));
            return actions;
        }

        // Skill 1/2/3: satu per tick, yang paling lama tidak dipakai dan siap. Bila HUD terbaca, cooldown/kunci dari HUD
        // (jeda minimal 400 ms agar HUD sempat berubah). v0.2.79 (jangan menebak): skill yang tidak ada di HUD / teks cd tak terbaca
        // TIDAK ditekan (dulu memakai CooldownMs dari pengaturan).
        // v0.2.65: Ultimate (G, "use when the ult bar is full") tidak ikut hitungan skill 1/2/3 / ganti power.
        var ult = skills.FirstOrDefault(IsUlt);
        var cds = s.SkillCooldowns.Where(c => ult is null || !c.Key.Equals(ult.Key, StringComparison.OrdinalIgnoreCase)).ToList();
        var hudCd = s.SkillCooldowns.GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        bool SkillReady(SkillBinding k) => hudCd.TryGetValue(k.Key, out var c)
            && c.Ready && (!_skillLast.TryGetValue(k.Name, out long t0) || now - t0 >= 400);
        var main = skills.Where(k => !IsDash(k) && !IsSwap(k) && !IsUlt(k)).ToList();
        // Ultimate: tekan G berkala selama bertarung (game menolak bila bar ult belum penuh); HUD dipakai bila ada kotak G.
        bool ultNow = ult is not null && now - _lastSkillPress >= 300
            && (s.UltReady ?? (hudCd.TryGetValue(ult.Key, out var uc) && uc.Ready))
            && (!_skillLast.TryGetValue(ult.Name, out long ut) || now - ut >= Math.Max(800, ult.CooldownMs))
            && HotkeyMap.TryGetVirtualKey(ult.Key, out _);
        var mainDue = main.Where(SkillReady)
            .OrderBy(k => _skillLast.TryGetValue(k.Name, out long t) ? t : long.MinValue)
            .FirstOrDefault();
        // Skill 1/2/3 didahulukan; G di sela-selanya.
        // Semua skill cooldown + ganti power aktif → T didahulukan (G jangan menelan animasi T).
        // HUD ult terbaca siap → G didahulukan; tidak terbaca → G hanya di sela skill dan tidak saat menunggu T.
        if (s.UltReady != true && (mainDue is not null || (swap is not null && cds.Count > 0 && cds.All(c => c.Cooling)))) ultNow = false;
        var due = ultNow ? ult : mainDue;
        // Basic attack (klik terus-menerus) bisa menelan tombol skill/T di game, jadi saat skill atau T ditekan,
        // basic attack dilewati di tick itu dan ditahan sebentar sesudahnya.
        if (due is not null && HotkeyMap.TryGetVirtualKey(due.Key, out uint vk))
        {
            _skillLast[due.Name] = now;
            _lastSkillPress = now;
            _attackHoldUntil = now + 250;
            // Saat animasi skill berjalan, Space ditolak game → lompat dulu (sambil roaming), lalu skill dilepas di udara.
            if (!ultNow && !approaching && S.Navigation.ChaseEnemies && now - _lastJump >= 600)
            {
                _lastJump = now;
                _nextRoamJump = now + 700 + _rng.Next(600);
                actions.Add(new KeyAction(VirtualKeys.Space, 90, "Lompat (sebelum skill)"));
            }
            actions.Add(new KeyAction((ushort)vk, 60, $"{due.Name} ({due.Key})"));
        }
        // Ganti power (T) hanya saat skill 1, 2, 3 di HUD semuanya cooldown/terkunci. T ditolak game selama animasi
        // serangan masih jalan, jadi: berhenti basic attack dulu (gerak tetap), tunggu animasi selesai, baru tekan T.
        // Bila HUD masih "semua cooldown" 1,2 dtk setelah T, ulangi (maks 3×), lalu jeda 4 dtk.
        else if (swap is not null && cds.Count > 0 && cds.All(c => c.Cooling)
                 && HotkeyMap.TryGetVirtualKey(swap.Key, out uint svk) && now >= _swapRestUntil)
        {
            if (_swapPrepSince is not long since)
            {
                _swapPrepSince = since = now;
                actions.Add(new NoteAction($"Semua skill cooldown → berhenti serang, siap {swap.Name} ({swap.Key})."));
            }
            _attackHoldUntil = now + 200;
            if (now - since >= SwapPrepMs && now - _swapLastPress >= 1200)
            {
                if (_swapTries >= 3) { _swapTries = 0; _swapPrepSince = null; _swapRestUntil = now + 4000; }
                else
                {
                    _swapTries++;
                    _swapLastPress = now;
                    actions.Add(new KeyAction((ushort)svk, 120, $"{swap.Name} ({swap.Key}): semua skill cooldown (coba {_swapTries})"));
                }
            }
        }
        else if (ResetSwap() && now >= _attackHoldUntil && now - _lastAttack >= Math.Max(30, S.Combat.AttackIntervalMs))
        {
            _lastAttack = now;
            actions.Add(new AttackAction("Serangan dasar"));
        }
        if (swap is null && cds.Count > 0 && cds.All(c => c.Cooling)
            && S.Combat.EffectiveSkills.FirstOrDefault(IsSwap) is { Enabled: false } off && Ready("swap-off", now, 15000))
            actions.Add(new NoteAction($"Semua skill cooldown, tapi {off.Name} ({off.Key}) mati di kartu Combat → nyalakan agar power berganti."));
        // Sudah dekat: tetap bergerak melingkari musuh + lompat (baik saat pakai skill maupun basic attack saja).
        Roam(s, S, tgt, now, actions);
        // Skill 1/2/3 habis (cooldown semua / tidak ada yang siap): Dash (Q) searah roaming agar tidak diam jadi sasaran.
        bool skillsOut = cds.Count > 0 && cds.All(c => c.Cooling);
        // Jangan Dash ≤ 500 ms sebelum T ditekan (animasi Dash bisa menelan T) atau di tick yang sama.
        bool swapSoon = _swapPrepSince is long ps && now >= _swapRestUntil && Math.Max(ps + SwapPrepMs, _swapLastPress + 1200) - now < 500;
        bool swapNow = swap is not null && actions.Any(a => a is KeyAction k && k.Reason.StartsWith(swap.Name, StringComparison.Ordinal));
        // Juga di sela skill: setelah animasi skill selesai dan tidak ada skill ditekan tick ini → Dash ke samping (arah roaming).
        bool skillGap = (due is null || ultNow) && now - _lastSkillPress >= SkillAnimMs && now - _lastJump >= 250;
        if ((skillsOut || skillGap) && dash is not null && s.DashCooling == false && !swapSoon && !swapNow
            && actions.OfType<MoveAction>().Any(m => m.Keys.Count > 0) && Ready("dash-roam", now, Math.Max(1500, dash.CooldownMs))
            && HotkeyMap.TryGetVirtualKey(dash.Key, out uint rvk))
            actions.Add(new KeyAction((ushort)rvk, 40, $"{dash.Name} ({dash.Key}) menghindar{(skillsOut ? ", skill habis" : " di sela skill")}"));
        return actions;
    }

    /// <summary>
    /// Sudah dalam jarak serang: jalan melingkari musuh (radius ~0,7× jarak serang, ±80°/dtk)
    /// sambil lompat tiap 0,9–1,6 dtk, bukan diam di tempat.
    /// </summary>
    private void Roam(GameSnapshot s, AppSettings S, EnemyInfo? target, long now, List<MacroAction> actions)
    {
        if (!S.Navigation.RoamWhenSkillsCooling || !S.Navigation.ChaseEnemies || target is null
            || s.PlayerPos is not { } p || s.CameraLook is not { } look) return;
        double r = Math.Max(2.5, S.Navigation.AttackRange);
        if (_roamSince is not long t0 || now - _roamLast > 1500) { t0 = now; _roamSince = now; _roamAngle = Math.Atan2(p.Z - target.Pos.Z, p.X - target.Pos.X); }
        _roamLast = now;
        // Arah putar kanan/kiri berganti acak tiap 2,5–4,5 dtk.
        if (now >= _roamFlipAt)
        {
            if (_roamFlipAt != 0) { _roamAngle += _roamDir * (now - t0) / 1000.0 * Math.Min(80 * Math.PI / 180, 12.0 / r); _roamSince = t0 = now; _roamDir = -_roamDir; }
            _roamFlipAt = now + 2500 + _rng.Next(2000);
        }
        // Kecepatan sudut diturunkan untuk lingkaran besar (± 12 stud/dtk di keliling); titik tuju 25° di depan agar jalur tetap dekat lingkaran.
        double omega = Math.Min(80 * Math.PI / 180, 12.0 / r);
        double a = _roamAngle + _roamDir * (Math.PI * 25 / 180 + (now - t0) / 1000.0 * omega);
        // Koreksi radial: terlalu dekat → titik tuju digeser keluar, terlalu jauh → ke dalam, agar jarak kembali ke r.
        double cur = Navigator.Distance2D(p.X, p.Z, target.Pos.X, target.Pos.Z);
        r = Math.Max(2.5, r + Math.Clamp(r - cur, -r * 0.5, r * 0.5));
        double gx = target.Pos.X + r * Math.Cos(a), gz = target.Pos.Z + r * Math.Sin(a);
        var keys = Navigator.KeysToward(p.X, p.Z, look.X, look.Z, gx, gz, 0.8);
        actions.RemoveAll(x => x is MoveAction);
        actions.Insert(0, new MoveAction(keys, "Roaming melingkari musuh"));
        // Lompat terjadwal ditunda bila skill baru ditekan (< 700 ms, animasi skill menelan Space) atau sudah lompat sebelum skill.
        if (now >= _nextRoamJump && now - _lastSkillPress >= SkillAnimMs && now - _lastJump >= 600)
        {
            _nextRoamJump = now + 700 + _rng.Next(600);
            _lastJump = now;
            // Lompat ditolak game selama animasi serang: buang klik serangan di tick ini dan tahan serangan 300 ms.
            actions.RemoveAll(x => x is AttackAction);
            _attackHoldUntil = Math.Max(_attackHoldUntil, now + 300);
            actions.Add(new KeyAction(VirtualKeys.Space, 100, "Lompat (roaming)"));
        }
    }
    /// <summary>Pita toleransi jarak serang (stud): maks(2, 10%).</summary>
    public static double RangeTolerance(double range) => Math.Max(2, range * 0.1);
    private long _attackHoldUntil, _swapLastPress = long.MinValue / 2, _swapRestUntil;
    private long? _swapPrepSince;
    private int _swapTries;
    /// <summary>Lama berhenti basic attack sebelum T agar animasi serangan selesai.</summary>
    private const int SwapPrepMs = 900;
    private bool ResetSwap() { _swapPrepSince = null; _swapTries = 0; return true; }
    private long? _roamSince;
    private long _roamLast, _nextRoamJump, _roamFlipAt, _lastJump = long.MinValue / 2, _lastSkillPress = long.MinValue / 2;
    /// <summary>Perkiraan lama animasi skill (Space/Dash ditolak game selama itu).</summary>
    private const int SkillAnimMs = 700;
    private int _roamDir = 1;
    private double _roamAngle;
    private readonly Random _rng = new();

    /// <summary>Jalan ke tujuan dengan W/A/S/D; tersangkut ≥1,5 dtk → lompat (+ serang bila diizinkan, untuk rubble).</summary>
    // ---------- Item di lantai (Workspace.PooledDrops) ----------
}

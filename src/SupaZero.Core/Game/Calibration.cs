using SupaZero.Core.Settings;

namespace SupaZero.Core.Game;

public enum CheckState { Ok, Warn, Fail, Skip }

/// <summary>Satu baris hasil kalibrasi: apa yang dicek, statusnya, dan nilai yang terbaca.</summary>
public sealed record CalibrationItem(string Name, CheckState State, string Value)
{
    public string Icon => State switch { CheckState.Ok => "✔", CheckState.Warn => "!", CheckState.Fail => "✖", _ => "–" };
    public override string ToString() => $"{Icon} {Name}: {Value}";
}

/// <summary>
/// Kalibrasi cepat: satu bacaan snapshot → daftar pengecekan (koneksi, stage, musuh, kamera, skill, power, trait, layar).
/// Item yang tidak berlaku di layar saat ini ditandai Skip, bukan gagal.
/// </summary>
public static class Calibration
{
    public static List<CalibrationItem> Run(GameSnapshot s, AppSettings settings)
    {
        var r = new List<CalibrationItem>();
        void Add(string n, CheckState st, string v) => r.Add(new(n, st, v));

        if (!s.Attached)
        {
            Add("Roblox", CheckState.Fail, "Tidak terhubung. Buka Roblox dulu.");
            return r;
        }
        Add("Roblox", s.PlaceId == AnimeZeroPaths.PlaceId ? CheckState.Ok : CheckState.Warn,
            s.PlaceId == AnimeZeroPaths.PlaceId ? "Anime Zero" : $"place {s.PlaceId} (bukan Anime Zero)");
        Add("Pemain", s.LocalPlayer.Length > 0 ? CheckState.Ok : CheckState.Fail, s.LocalPlayer.Length > 0 ? s.LocalPlayer : "LocalPlayer tidak terbaca");
        Add("Layar", s.Screen == GameScreen.Unknown ? CheckState.Warn : CheckState.Ok, s.Screen.ToString());
        Add("Posisi karakter", s.PlayerPos is null ? CheckState.Fail : CheckState.Ok,
            s.PlayerPos is { } p ? $"{p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}" : "HumanoidRootPart tidak terbaca");

        // Kamera
        if (s.Camera is { } cam)
        {
            double pitch = Math.Atan2(cam.Look.Y, Math.Sqrt(cam.Look.X * cam.Look.X + cam.Look.Z * cam.Look.Z)) * 180 / Math.PI;
            bool sizeOk = s.ClientW <= 0 || (Math.Abs(cam.ViewW - s.ClientW) <= 4 && Math.Abs(cam.ViewH - s.ClientH) <= 40);
            Add("Kamera", sizeOk ? CheckState.Ok : CheckState.Warn,
                $"FOV {cam.Fov:0}, pitch {pitch:0}°, viewport {cam.ViewW:0}x{cam.ViewH:0}" + (s.ClientW > 0 ? $" (jendela {s.ClientW}x{s.ClientH})" : ""));
            if (pitch > 20) Add("Arah kamera", CheckState.Warn, $"Kamera mendongak {pitch:0}°; bidikan bisa meleset.");
        }
        else Add("Kamera", CheckState.Fail, "Workspace.Camera tidak terbaca");
        Add("Shift lock", CheckState.Ok, s.ShiftLock ? "Aktif (bidik dengan putar kamera)" : "Tidak aktif / belum terdeteksi");

        // Stage / HUD
        bool inRound = s.Screen == GameScreen.InRound;
        if (s.Hud is { } h)
            Add("Stage", CheckState.Ok, $"{h.Difficulty ?? "?"} · Area {h.Area}/{h.AreaTotal} · {h.EnemiesLeft} musuh{(h.StoryComplete ? " · Story tamat" : "")}");
        else Add("Stage", inRound ? CheckState.Fail : CheckState.Skip, inRound ? $"HUD tidak terbaca ({s.HudRaw ?? "kosong"})" : "Hanya di dalam ronde");

        // Musuh
        if (inRound || s.Enemies.Count > 0)
        {
            var alive = s.Enemies.Where(e => e.Alive).ToList();
            if (alive.Count == 0) Add("Musuh", inRound ? CheckState.Warn : CheckState.Skip, "Tidak ada musuh hidup di Workspace.enemies");
            else
            {
                var me = s.PlayerPos;
                var near = me is { } m ? alive.OrderBy(e => Dist(m, e.Pos)).First() : alive[0];
                string d = me is { } m2 ? $"{Dist(m2, near.Pos):0} stud" : "jarak ?";
                Add("Musuh", CheckState.Ok, $"{alive.Count} hidup · terdekat #{near.Name} {d}, HP {near.Health:0}/{near.MaxHealth:0}");
                if (s.Camera?.WorldToScreen(near.Pos) is { } sp)
                {
                    bool onScreen = sp.X >= 0 && sp.Y >= 0 && sp.X < s.Camera.ViewW && sp.Y < s.Camera.ViewH;
                    Add("Bidikan", onScreen ? CheckState.Ok : CheckState.Warn, onScreen ? $"Musuh terdekat di layar ({sp.X:0}, {sp.Y:0})" : "Musuh terdekat di luar layar");
                }
                else Add("Bidikan", CheckState.Warn, "Musuh terdekat di belakang kamera");
            }
        }
        else Add("Musuh", CheckState.Skip, "Hanya di dalam ronde");

        // Skill HUD
        if (s.SkillCooldowns.Count > 0)
            Add("Skill HUD", CheckState.Ok, string.Join(", ", s.SkillCooldowns.Select(c =>
                $"{c.Key}: {(c.Locked ? "terkunci" : c.Remaining is { } t ? $"{t:0.#} dtk" : "siap")}")));
        else Add("Skill HUD", inRound ? CheckState.Fail : CheckState.Skip, inRound ? "skillBox tidak terbaca" : "Hanya di dalam ronde");
        if (inRound) Add("Dash", CheckState.Ok, s.DashCooling ? "Cooldown" : "Siap");

        var skills = settings.Combat.EffectiveSkills;
        foreach (var k in skills.Where(k => k.Name is "Dash" or "Ganti power" or "Ganti senjata"))
            Add($"Toggle {k.Name} ({k.Key})", k.Enabled ? CheckState.Ok : CheckState.Warn, k.Enabled ? "Nyala" : "Mati di kartu Combat");

        // Power & trait
        var eq = s.Equipped.Count > 0 ? s.Equipped : settings.PowerLoadout ?? [];
        if (eq.Count == 0) Add("Power", CheckState.Warn, "Belum terbaca (buka menu karakter sekali di lobby)");
        foreach (var pw in eq.OrderBy(x => x.Loadout))
        {
            Add($"Power slot {pw.Loadout}", CheckState.Ok, $"{pw.Name}{(pw.Level is { } lv ? $" Lv.{lv}" : "")}{(pw.Rarity is { Length: > 0 } ra ? $" · {ra}" : "")}");
            var tr = pw.Trait ?? (settings.PowerTraits.TryGetValue(pw.Name, out var t0) ? t0 : null);
            Add($"Trait slot {pw.Loadout}", tr is null ? CheckState.Warn : CheckState.Ok,
                tr is { } t ? $"{t.Name}{(t.Chance is { } c ? $" ({c})" : "")}{(t.Buffs.Count > 0 ? " · " + string.Join(" ", t.Buffs) : "")}" : "Belum terbaca (buka menu Trait sekali)");
        }

        // Layar khusus
        if (s.Screen == GameScreen.Lobby && s.PlayerPos is { } lp)
        {
            var rooms = s.StartRooms.Select(x => (Name: $"room {x.Id}{(x.Occupied ? " (terisi)" : "")}", x.Pos))
                .Concat(s.StartCircles.Select((c, i) => (Name: $"Start Here {i + 1}", Pos: c))).ToList();
            if (rooms.Count == 0) Add("Lingkaran room", CheckState.Fail, "QuqueRooms / Start Here tidak terbaca");
            else
            {
                var n = rooms.OrderBy(x => Dist(lp, x.Pos)).First();
                double d = Dist(lp, n.Pos);
                Add("Lingkaran room", d <= 45 ? CheckState.Ok : CheckState.Warn, $"{rooms.Count} terbaca · terdekat {n.Name} {d:0} stud{(d > 45 ? " (jauh → teleport)" : "")}");
            }
        }
        if (s.Screen == GameScreen.Lobby)
            Add("Lobby", s.AreasButton is not null || s.StartRooms.Count > 0 ? CheckState.Ok : CheckState.Warn,
                $"Tombol Areas {(s.AreasButton is null ? "tidak" : "")} terbaca · {s.StartRooms.Count} room · {s.TeleportCards.Count} kartu teleport".Replace("  ", " "));
        if (s.Screen == GameScreen.LobbyMenu)
            Add("Menu Create Room", s.LobbyStart is not null ? CheckState.Ok : CheckState.Warn,
                $"{s.LobbyMaps.Count} map · {s.LobbyActs.Count} act · {s.LobbyDifficulties.Count} difficulty · Start {(s.LobbyStart is null ? "tidak terbaca" : "ok")}");
        if (s.Screen is GameScreen.Victory or GameScreen.Defeat)
            Add("Layar hasil", s.NextButton is not null || s.RetryButton is not null ? CheckState.Ok : CheckState.Warn,
                $"{s.ResultText ?? "?"} · Next {(s.NextButton is null ? "-" : "ok")} · Retry {(s.RetryButton is null ? "-" : "ok")} · Leave {(s.LeaveButton is null ? "-" : "ok")}");
        if (s.Screen == GameScreen.QueueWaiting)
            Add("Antrean", CheckState.Ok, s.QueueSkip is null ? "Skip Waiting tidak tampil" : s.QueueSkipCosts ? "Skip Waiting berbayar (tidak diklik)" : "Skip Waiting gratis");
        return r;
    }

    public static string Summary(IReadOnlyList<CalibrationItem> items)
    {
        int ok = items.Count(i => i.State == CheckState.Ok), warn = items.Count(i => i.State == CheckState.Warn), fail = items.Count(i => i.State == CheckState.Fail);
        return $"{ok} OK · {warn} peringatan · {fail} gagal";
    }

    private static double Dist(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}

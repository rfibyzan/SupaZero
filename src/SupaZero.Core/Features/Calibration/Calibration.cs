using SupaZero.Core.Settings;

namespace SupaZero.Core.Game;

public enum CheckState { Ok, Warn, Fail, Skip }

/// <summary>One calibration result row: what was checked, its state, and the value read.</summary>
public sealed record CalibrationItem(string Name, CheckState State, string Value)
{
    public string Icon => State switch { CheckState.Ok => "✔", CheckState.Warn => "!", CheckState.Fail => "✖", _ => "–" };
    public override string ToString() => $"{Icon} {Name}: {Value}";
}

/// <summary>
/// Quick calibration: one snapshot read → list of checks (connection, stage, enemies, camera, skills, power, trait, screen).
/// Items that do not apply to the current screen are marked Skip, not failed.
/// </summary>
public static class Calibration
{
    public static List<CalibrationItem> Run(GameSnapshot s, AppSettings settings)
    {
        var r = new List<CalibrationItem>();
        void Add(string n, CheckState st, string v) => r.Add(new(n, st, v));

        if (!s.Attached)
        {
            Add("Roblox", CheckState.Fail, "Not attached. Open Roblox first.");
            return r;
        }
        Add("Roblox", s.PlaceId == AnimeZeroPaths.PlaceId ? CheckState.Ok : CheckState.Warn,
            s.PlaceId == AnimeZeroPaths.PlaceId ? "Anime Zero" : $"place {s.PlaceId} (not Anime Zero)");
        Add("Player", s.LocalPlayer.Length > 0 ? CheckState.Ok : CheckState.Fail, s.LocalPlayer.Length > 0 ? s.LocalPlayer : "LocalPlayer not readable");
        Add("Screen", s.Screen == GameScreen.Unknown ? CheckState.Warn : CheckState.Ok, s.Screen.ToString());
        Add("Character position", s.PlayerPos is null ? CheckState.Fail : CheckState.Ok,
            s.PlayerPos is { } p ? $"{p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}" : "HumanoidRootPart not readable");

        // Camera
        if (s.Camera is { } cam)
        {
            double pitch = Math.Atan2(cam.Look.Y, Math.Sqrt(cam.Look.X * cam.Look.X + cam.Look.Z * cam.Look.Z)) * 180 / Math.PI;
            bool sizeOk = s.ClientW <= 0 || (Math.Abs(cam.ViewW - s.ClientW) <= 4 && Math.Abs(cam.ViewH - s.ClientH) <= 40);
            Add("Camera", sizeOk ? CheckState.Ok : CheckState.Warn,
                $"FOV {cam.Fov:0}, pitch {pitch:0}°, viewport {cam.ViewW:0}x{cam.ViewH:0}" + (s.ClientW > 0 ? $" (window {s.ClientW}x{s.ClientH})" : ""));
            if (pitch > 20) Add("Camera direction", CheckState.Warn, $"Camera is looking up {pitch:0}°; aim may miss.");
        }
        else Add("Camera", CheckState.Fail, "Workspace.Camera not readable");
        Add("Shift lock", CheckState.Ok, s.ShiftLock ? "On (aim by turning the camera)" : "Off / not detected yet");

        // Stage / HUD
        bool inRound = s.Screen == GameScreen.InRound;
        if (s.Hud is { } h)
            Add("Stage", CheckState.Ok, $"{h.Difficulty ?? "?"} · Area {h.Area}/{h.AreaTotal} · {h.EnemiesLeft} enemies{(h.StoryComplete ? " · Story complete" : "")}");
        else Add("Stage", inRound ? CheckState.Fail : CheckState.Skip, inRound ? $"HUD not readable ({s.HudRaw ?? "empty"})" : "Only inside a round");

        // Enemies
        if (inRound || s.Enemies.Count > 0)
        {
            var alive = s.Enemies.Where(e => e.Alive).ToList();
            if (alive.Count == 0) Add("Enemies", inRound ? CheckState.Warn : CheckState.Skip, "No live enemies in Workspace.enemies");
            else
            {
                var me = s.PlayerPos;
                var near = me is { } m ? alive.OrderBy(e => Dist(m, e.Pos)).First() : alive[0];
                string d = me is { } m2 ? $"{Dist(m2, near.Pos):0} stud" : "distance ?";
                Add("Enemies", CheckState.Ok, $"{alive.Count} alive · nearest #{near.Name} {d}, HP {near.Health:0}/{near.MaxHealth:0}");
                if (s.Camera?.WorldToScreen(near.Pos) is { } sp)
                {
                    bool onScreen = sp.X >= 0 && sp.Y >= 0 && sp.X < s.Camera.ViewW && sp.Y < s.Camera.ViewH;
                    Add("Aim", onScreen ? CheckState.Ok : CheckState.Warn, onScreen ? $"Nearest enemy on screen ({sp.X:0}, {sp.Y:0})" : "Nearest enemy off screen");
                }
                else Add("Aim", CheckState.Warn, "Nearest enemy behind the camera");
            }
        }
        else Add("Enemies", CheckState.Skip, "Only inside a round");

        // Skill HUD
        if (s.SkillCooldowns.Count > 0)
            Add("Skill HUD", CheckState.Ok, string.Join(", ", s.SkillCooldowns.Select(c =>
                $"{c.Key}: {(c.Locked ? "locked" : c.Remaining is { } t ? (double.IsNaN(t) ? "not readable" : $"{t:0.#} s") : "ready")}")));
        else Add("Skill HUD", inRound ? CheckState.Fail : CheckState.Skip, inRound ? "skillBox not readable" : "Only inside a round");
        if (inRound) Add("Dash", s.DashCooling is null ? CheckState.Fail : CheckState.Ok, s.DashCooling switch { true => "Cooldown", false => "Ready", null => "Dash button not readable" });

        var skills = settings.Combat.EffectiveSkills;
        foreach (var k in skills.Where(k => k.Name is "Dash" or "Swap power" or "Ganti power" or "Ganti senjata"))
            Add($"Toggle {k.Name} ({k.Key})", k.Enabled ? CheckState.Ok : CheckState.Warn, k.Enabled ? "On" : "Off in the Combat card");

        // Power & trait
        var eq = s.Equipped.Count > 0 ? s.Equipped : settings.PowerLoadout ?? [];
        if (eq.Count == 0) Add("Power", CheckState.Warn, "Not read yet (open the character menu once in the lobby)");
        foreach (var pw in eq.OrderBy(x => x.Loadout))
        {
            Add($"Power slot {pw.Loadout}", CheckState.Ok, $"{pw.Name}{(pw.Level is { } lv ? $" Lv.{lv}" : "")}{(pw.Rarity is { Length: > 0 } ra ? $" · {ra}" : "")}");
            var tr = pw.Trait ?? (settings.PowerTraits.TryGetValue(pw.Name, out var t0) ? t0 : null);
            Add($"Trait slot {pw.Loadout}", tr is null ? CheckState.Warn : CheckState.Ok,
                tr is { } t ? $"{t.Name}{(t.Chance is { } c ? $" ({c})" : "")}{(t.Buffs.Count > 0 ? " · " + string.Join(" ", t.Buffs) : "")}" : "Not read yet (open the Trait menu once)");
        }

        // Screen-specific
        if (s.Screen == GameScreen.Lobby && s.PlayerPos is { } lp)
        {
            var rooms = s.StartRooms.Select(x => (Name: $"room {x.Id}{(x.Occupied ? " (occupied)" : "")}", x.Pos))
                .Concat(s.StartCircles.Select((c, i) => (Name: $"Start Here {i + 1}", Pos: c))).ToList();
            if (rooms.Count == 0) Add("Room circle", CheckState.Fail, "QuqueRooms / Start Here not readable");
            else
            {
                var n = rooms.OrderBy(x => Dist(lp, x.Pos)).First();
                double d = Dist(lp, n.Pos);
                Add("Room circle", d <= 45 ? CheckState.Ok : CheckState.Warn, $"{rooms.Count} read · nearest {n.Name} {d:0} stud{(d > 45 ? " (far → teleport)" : "")}");
            }
        }
        if (s.Screen == GameScreen.Lobby)
            Add("Lobby", s.AreasButton is not null || s.StartRooms.Count > 0 ? CheckState.Ok : CheckState.Warn,
                $"Areas button {(s.AreasButton is null ? "not" : "")} read · {s.StartRooms.Count} room · {s.TeleportCards.Count} teleport cards".Replace("  ", " "));
        if (s.Screen == GameScreen.LobbyMenu)
            Add("Menu Create Room", s.LobbyStart is not null ? CheckState.Ok : CheckState.Warn,
                $"{s.LobbyMaps.Count} map · {s.LobbyActs.Count} act · {s.LobbyDifficulties.Count} difficulty · Start {(s.LobbyStart is null ? "not readable" : "ok")}");
        if (s.Screen is GameScreen.Victory or GameScreen.Defeat)
            Add("Result screen", s.NextButton is not null || s.RetryButton is not null ? CheckState.Ok : CheckState.Warn,
                $"{s.ResultText ?? "?"} · Next {(s.NextButton is null ? "-" : "ok")} · Retry {(s.RetryButton is null ? "-" : "ok")} · Leave {(s.LeaveButton is null ? "-" : "ok")}");
        if (s.Screen == GameScreen.QueueWaiting)
            Add("Queue", CheckState.Ok, s.QueueSkip is null ? "Skip Waiting not shown" : s.QueueSkipCosts ? "Skip Waiting costs (not clicked)" : "Skip Waiting free");
        return r;
    }

    public static string Summary(IReadOnlyList<CalibrationItem> items)
    {
        int ok = items.Count(i => i.State == CheckState.Ok), warn = items.Count(i => i.State == CheckState.Warn), fail = items.Count(i => i.State == CheckState.Fail);
        return $"{ok} OK · {warn} warnings · {fail} failed";
    }

    private static double Dist(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}

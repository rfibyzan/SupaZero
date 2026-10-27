using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    /// <summary>
    /// RIE snapshot while rubble is active (Byzan, 24 Oct 2026, Hxh Area 3→4): BillboardGui PlayerGui.RubbleBar is attached to
    /// Attachment "RubbleBar" in Workspace.World.Map.Border2.Blocker (101x57x4 wall Part in front of the rubble).
    /// Target = the point on the Blocker wall nearest the player (at player height), not the wall center.
    /// </summary>
    private Settings.Vec3? ReadRubble(long pg, long ws, string? hudRaw, Settings.Vec3? me, bool voteShown = false, bool enterHint = false)
    {
        _rubbleInfo = null; _rubbleHp = null; _rubbleStand = null; _rubbleArmed = false;
        if (pg == 0) return null;
        bool hint = hudRaw?.Contains("rubble", StringComparison.OrdinalIgnoreCase) == true;
        long bossBar = reader.FindChild(pg, AnimeZeroPaths.RubbleBossBar);
        long bar = reader.FindChild(pg, AnimeZeroPaths.RubbleBar);
        // v0.2.63: rubble breaks by itself if not hit for a long time. Without the HUD text "Break the rubble", rubble is still considered active
        // when the RubbleBar BillboardGui exists and the Blocker it is attached to is still solid (CanCollide true), so it gets attacked right away.
        bool sure = hint || (bossBar != 0 && IsShown(bossBar, pg));
        // v0.2.73 (Byzan rule 24 Oct 22:54): rubble is ONLY attacked when the top HUD text is "Break the rubble to continue!"
        // (or RubbleBossBar is shown). The v0.2.63 fallback (solid Blocker + RubbleBar) wrongly triggered before the wave started.
        // v0.3.12 (Byzan 26 Oct): rubble exists from the start and can be hit after the area wave is clear → without HUD text the position
        // is still read as "pending rubble" (RubblePending) when the RubbleBar Adornee Blocker is still solid; FarmMachine
        // decides (only after this area's wave has been seen and is cleared).
        _rubbleArmed = sure;
        string info = "RubbleBar missing";
        long holder = 0;
        if (bar != 0)
        {
            // Rubble HP: text "87%" in the RubbleBar BillboardGui.
            foreach (var t in Texts(bar, 0))
            {
                var m = System.Text.RegularExpressions.Regex.Match(t, @"(\d+(?:[.,]\d+)?)\s*%");
                if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                { _rubbleHp = pct / 100.0; break; }
            }
            if (!reader.Offsets.TryResolve("Misc.Adornee", out long ao)) info = "offset Misc.Adornee missing";
            else if (!M.TryReadPointer(bar + ao, out long ad) || ad == 0) info = "Adornee empty";
            else
            {
                string cls = reader.GetClassName(ad);
                info = $"Adornee {cls} {reader.GetName(ad)}";
                // An Attachment has no Position of its own → use its parent Part (Blocker).
                holder = cls == "Attachment" ? reader.GetParent(ad) : ad;
            }
        }
        // v0.3.20 (Byzan RIE World 13:55:08 + log 27 Oct 2026, Hxh Act 4): RubbleBar.Adornee is empty on this map, but only the active
        // Border*.Blocker carries an Attachment named "RubbleBar" (Border1 had it, Border2-4 Blockers had no children). Fallback ONLY when
        // exactly one solid Blocker has that Attachment; zero or several = unknown → unreadable, as before.
        if (holder == 0 && ws != 0 && UniqueRubbleBlocker(ws) is { } ub)
        {
            holder = ub.Blocker;
            info += $"; Blocker {ub.Border} (only solid Blocker with RubbleBar Attachment)";
        }
        if (!sure)
        {
            // Rubble destroyed → Blocker CanCollide false (v0.2.91). Without a solid Blocker there is no pending rubble.
            if (holder == 0 || !string.Equals(Prop(holder, "CanCollide"), "true", StringComparison.OrdinalIgnoreCase)) { _rubbleInfo = null; return null; }
            info += "; pending (Blocker solid, HUD does not mention rubble yet)";
        }
        // v0.2.80 (no guessing): rubble position ONLY from the RubbleBar Adornee. The fallback "first Border*.Blocker that has a
        // RubbleBar" was removed: every Blocker has a RubbleBar from the start of the stage, so picking one is a guess.
        if (holder != 0 && props.TryGetWorldPosition(holder, out float x, out float y, out float z))
        {
            var c = new Settings.Vec3(x, y, z);
            _rubbleInfo = info;
            if (me is { } p && ParseVec(Prop(holder, "Size")) is { } sz && sz.X > 0)
            {
                // v0.2.91: Hxh Border3/Border4 Blockers are rotated 90° (LookVector 1,0,0) → the nearest point must follow the rotation.
                var lv = ParseVec(Prop(holder, "LookVector"));
                _rubbleStand = RubbleStandPoint(holder, p);
                if (_rubbleStand is { } st)
                    _rubbleInfo += string.Create(System.Globalization.CultureInfo.InvariantCulture, $"; stand at ClimbStop ({st.X:0},{st.Z:0})");
                return RubbleTarget(c, sz, p, lv?.X ?? 0, lv?.Z ?? -1);
            }
            return c;
        }
        if (holder != 0 && PositionOf(holder) is { } hp) { _rubbleInfo = info; return hp; }
        _rubbleInfo = $"{info}; position unreadable";
        return null;
    }

    /// <summary>v0.3.20: the single Workspace.World.Map.Border*.Blocker (CanCollide true) that has a child Attachment "RubbleBar"; null when 0 or ≥2.</summary>
    private (long Blocker, string Border)? UniqueRubbleBlocker(long ws)
    {
        long world = reader.FindChild(ws, AnimeZeroPaths.World);
        long map = world != 0 ? reader.FindChild(world, AnimeZeroPaths.WorldMap) : 0;
        if (map == 0) return null;
        (long, string)? found = null;
        foreach (long b in reader.GetChildren(map, 512))
        {
            string bn = reader.GetName(b);
            if (!bn.StartsWith("Border", StringComparison.Ordinal)) continue;
            long blk = reader.FindChild(b, AnimeZeroPaths.RubbleBlocker);
            if (blk == 0 || !string.Equals(Prop(blk, "CanCollide"), "true", StringComparison.OrdinalIgnoreCase)) continue;
            long att = reader.FindChild(blk, AnimeZeroPaths.RubbleBar);
            if (att == 0 || reader.GetClassName(att) != "Attachment") continue;
            if (found is not null) return null; // several candidates = guess → refuse
            found = (blk, bn);
        }
        return found;
    }

    private Settings.Vec3? _rubbleStand;
    private bool _rubbleArmed;

    /// <summary>
    /// Point on the wall box nearest the player (following LookVector rotation), Y = player Y.
    /// Size.X along RightVector (-lz, lx), Size.Z along LookVector (lx, lz). Default LookVector (0,0,-1) = axis-aligned.
    /// </summary>
    public static Settings.Vec3 RubbleTarget(Settings.Vec3 center, Settings.Vec3 size, Settings.Vec3 me, double lookX = 0, double lookZ = -1)
    {
        double l = Math.Sqrt(lookX * lookX + lookZ * lookZ);
        if (l < 1e-6) { lookX = 0; lookZ = -1; l = 1; }
        lookX /= l; lookZ /= l;
        double rx = -lookZ, rz = lookX;
        double dx = me.X - center.X, dz = me.Z - center.Z;
        double u = Math.Clamp(dx * rx + dz * rz, -Math.Abs(size.X) / 2, Math.Abs(size.X) / 2);
        double v = Math.Clamp(dx * lookX + dz * lookZ, -Math.Abs(size.Z) / 2, Math.Abs(size.Z) / 2);
        return new(center.X + u * rx + v * lookX, me.Y, center.Z + u * rz + v * lookZ);
    }

    /// <summary>
    /// v0.2.91 (Byzan RIE snapshot 25 Oct 00:59, Hxh Border4): in front of the Blocker is Border*.Rubble.ClimbStop (a solid Part
    /// 200 studs tall, ~20 studs in front of the Border4 wall) → the character can never get ≤ 12 studs from the Blocker. Standing point =
    /// nearest ClimbStop point (same Border as the RubbleBar Adornee Blocker). null when no ClimbStop is readable.
    /// </summary>
    private Settings.Vec3? RubbleStandPoint(long blocker, Settings.Vec3 me)
    {
        long border = reader.GetParent(blocker);
        long rub = border != 0 ? reader.FindChild(border, AnimeZeroPaths.RubbleName) : 0;
        if (rub == 0) return null;
        Settings.Vec3? best = null; double bd = double.MaxValue;
        foreach (long c in reader.GetChildren(rub, 256))
        {
            if (reader.GetName(c) != AnimeZeroPaths.RubbleClimbStop) continue;
            if (!props.TryGetWorldPosition(c, out float x, out float y, out float z)) continue;
            if (ParseVec(Prop(c, "Size")) is not { } sz) continue;
            var lv = ParseVec(Prop(c, "LookVector"));
            var t = RubbleTarget(new(x, y, z), sz, me, lv?.X ?? 0, lv?.Z ?? -1);
            double d = (t.X - me.X) * (t.X - me.X) + (t.Z - me.Z) * (t.Z - me.Z);
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }
}

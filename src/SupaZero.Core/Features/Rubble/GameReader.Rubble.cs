using RIE.Core.Macro;
using RIE.Core.Memory;
using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    /// <summary>
    /// Snapshot RIE saat rubble aktif (Byzan, 24 Okt 2026, Hxh Area 3→4): BillboardGui PlayerGui.RubbleBar menempel ke
    /// Attachment "RubbleBar" di Workspace.World.Map.Border2.Blocker (Part dinding 101x57x4 di depan rubble).
    /// Target = titik dinding Blocker terdekat ke pemain (setinggi pemain), bukan pusat dinding.
    /// </summary>
    private Settings.Vec3? ReadRubble(long pg, long ws, string? hudRaw, Settings.Vec3? me, bool voteShown = false, bool enterHint = false)
    {
        _rubbleInfo = null; _rubbleHp = null; _rubbleStand = null; _rubbleArmed = false;
        if (pg == 0) return null;
        bool hint = hudRaw?.Contains("rubble", StringComparison.OrdinalIgnoreCase) == true;
        long bossBar = reader.FindChild(pg, AnimeZeroPaths.RubbleBossBar);
        long bar = reader.FindChild(pg, AnimeZeroPaths.RubbleBar);
        // v0.2.63: rubble hancur sendiri bila lama tidak dipukul. Tanpa teks HUD "Break the rubble", rubble tetap dianggap aktif
        // bila BillboardGui RubbleBar ada dan Blocker yang ditempelinya masih padat (CanCollide true), agar langsung diserang.
        bool sure = hint || (bossBar != 0 && IsShown(bossBar, pg));
        // v0.2.73 (aturan Byzan 24 Okt 22:54): rubble HANYA diserang saat teks HUD atas "Break the rubble to continue!"
        // (atau RubbleBossBar tampil). Cadangan v0.2.63 (Blocker padat + RubbleBar) salah aktif sebelum wave mulai.
        // v0.3.12 (Byzan 26 Okt): rubble sudah ada sejak awal dan bisa dipukul setelah wave area bersih → tanpa teks HUD posisi
        // tetap dibaca sebagai "rubble menunggu" (RubblePending) bila Blocker Adornee RubbleBar masih padat; FarmMachine yang
        // memutuskan (hanya setelah wave area ini terlihat dan habis).
        _rubbleArmed = sure;
        string info = "RubbleBar tidak ada";
        long holder = 0;
        if (bar != 0)
        {
            // Nyawa rubble: teks "87%" di BillboardGui RubbleBar.
            foreach (var t in Texts(bar, 0))
            {
                var m = System.Text.RegularExpressions.Regex.Match(t, @"(\d+(?:[.,]\d+)?)\s*%");
                if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                { _rubbleHp = pct / 100.0; break; }
            }
            if (!reader.Offsets.TryResolve("Misc.Adornee", out long ao)) info = "offset Misc.Adornee tidak ada";
            else if (!M.TryReadPointer(bar + ao, out long ad) || ad == 0) info = "Adornee kosong";
            else
            {
                string cls = reader.GetClassName(ad);
                info = $"Adornee {cls} {reader.GetName(ad)}";
                // Attachment tidak punya Position sendiri → pakai Part induknya (Blocker).
                holder = cls == "Attachment" ? reader.GetParent(ad) : ad;
            }
        }
        if (!sure)
        {
            // Rubble hancur → Blocker CanCollide false (v0.2.91). Tanpa Blocker padat tidak ada rubble menunggu.
            if (holder == 0 || !string.Equals(Prop(holder, "CanCollide"), "true", StringComparison.OrdinalIgnoreCase)) { _rubbleInfo = null; return null; }
            info += "; menunggu (Blocker padat, HUD belum menyebut rubble)";
        }
        // v0.2.80 (jangan menebak): posisi rubble HANYA dari Adornee RubbleBar. Cadangan "Border*.Blocker pertama yang punya
        // RubbleBar" dihapus: semua Blocker punya RubbleBar sejak awal stage, jadi pilihannya tebakan.
        if (holder != 0 && props.TryGetWorldPosition(holder, out float x, out float y, out float z))
        {
            var c = new Settings.Vec3(x, y, z);
            _rubbleInfo = info;
            if (me is { } p && ParseVec(Prop(holder, "Size")) is { } sz && sz.X > 0)
            {
                // v0.2.91: Blocker Border3/Border4 Hxh diputar 90° (LookVector 1,0,0) → titik terdekat wajib ikut rotasi.
                var lv = ParseVec(Prop(holder, "LookVector"));
                _rubbleStand = RubbleStandPoint(holder, p);
                if (_rubbleStand is { } st)
                    _rubbleInfo += string.Create(System.Globalization.CultureInfo.InvariantCulture, $"; berdiri di ClimbStop ({st.X:0},{st.Z:0})");
                return RubbleTarget(c, sz, p, lv?.X ?? 0, lv?.Z ?? -1);
            }
            return c;
        }
        if (holder != 0 && PositionOf(holder) is { } hp) { _rubbleInfo = info; return hp; }
        _rubbleInfo = $"{info}; posisi tidak terbaca";
        return null;
    }

    private Settings.Vec3? _rubbleStand;
    private bool _rubbleArmed;

    /// <summary>
    /// Titik terdekat di kotak dinding ke pemain (ikut rotasi LookVector), Y = Y pemain.
    /// Size.X searah RightVector (-lz, lx), Size.Z searah LookVector (lx, lz). Default LookVector (0,0,-1) = rata sumbu.
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
    /// v0.2.91 (snapshot RIE Byzan 25 Okt 00:59, Hxh Border4): di depan Blocker ada Border*.Rubble.ClimbStop (Part padat
    /// 200 stud tinggi, ~20 stud di depan dinding Border4) → karakter tidak pernah bisa ≤ 12 stud dari Blocker. Titik berdiri =
    /// titik ClimbStop terdekat (Border yang sama dengan Blocker Adornee RubbleBar). null bila tidak ada ClimbStop terbaca.
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

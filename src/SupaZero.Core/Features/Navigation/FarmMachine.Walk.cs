using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    private readonly PathPlanner _planner = new();
    private List<Vec3>? _path;
    private Vec3? _pathGoal;
    private long _pathAt;
    private int _enterArea, _enterDepth;
    private long _enterNearSince;

    /// <summary>
    /// Tujuan "Enter area N". Bila sudah sampai di titik masuk tapi HUD masih "Enter area N" (pemicu game ada lebih dalam,
    /// mis. Hxh Area 2 berhenti ~31 stud dari penanda), maju bertahap 15 stud tiap 1,5 dtk ke pusat kotak Areas.N.
    /// </summary>
    private Settings.Vec3? EnterTarget(GameSnapshot s, AppSettings S, long now)
    {
        if (s.NextAreaPos is not { } baseP) return null;
        var hud = s.Hud;
        int n = hud?.TargetArea ?? 0;
        if (n != _enterArea || hud is not { Enter: true }) { _enterArea = n; _enterDepth = 0; _enterNearSince = 0; }
        Settings.Vec3 Target()
        {
            if (_enterDepth == 0 || s.NextAreaCenter is not { } c) return baseP;
            double dx = c.X - baseP.X, dz = c.Z - baseP.Z, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1) return baseP;
            double t = Math.Min(len, _enterDepth * 15.0) / len;
            return new(baseP.X + dx * t, baseP.Y, baseP.Z + dz * t);
        }
        var tgt = Target();
        if (hud is { Enter: true } && s.PlayerPos is { } p)
        {
            double d = Math.Sqrt((p.X - tgt.X) * (p.X - tgt.X) + (p.Z - tgt.Z) * (p.Z - tgt.Z));
            if (d <= S.Navigation.ArriveRadius + 3)
            {
                if (_enterNearSince == 0) _enterNearSince = now;
                else if (now - _enterNearSince >= 1500) { _enterDepth++; _enterNearSince = 0; _path = null; tgt = Target(); }
            }
            else _enterNearSince = 0;
        }
        return tgt;
    }
    private string? _pathNote;

    /// <summary>
    /// Jalan ke tujuan lewat jalur, bukan garis lurus: bila garis lurus terhalang rintangan atau keluar area, A* mencari rute
    /// (mis. memutar huruf U), lalu karakter berjalan dari titik belok ke titik belok. Tanpa peta / jalur → <see cref="WalkTo"/>.
    /// </summary>
    private List<MacroAction> WalkPath(GameSnapshot s, Vec3 target, double radius, long now, string label, bool attackWhenStuck)
    {
        if (s.Nav is not { IsEmpty: false } nav || s.PlayerPos is not { } p
            || Navigator.Distance2D(p.X, p.Z, target.X, target.Z) <= Math.Max(radius, 6))
        { _path = null; return WalkTo(s, target, radius, now, label, attackWhenStuck); }
        bool stuck = _stuckRef is not null && now - _stuckSince >= 1500;
        bool goalMoved = _pathGoal is not { } g || Navigator.Distance2D(g.X, g.Z, target.X, target.Z) > 6;
        // Tersangkut di objek yang tidak ada di peta: tandai titik ~2,5 stud di depan (arah titik belok) sebagai rintangan lalu cari ulang.
        if (stuck && _path is { Count: > 0 } cur && now - _pathAt > 1500)
        {
            var w0 = cur[0];
            double d = Math.Max(0.01, Navigator.Distance2D(p.X, p.Z, w0.X, w0.Z));
            _planner.Block(p.X + (w0.X - p.X) / d * 2.5, p.Z + (w0.Z - p.Z) / d * 2.5);
        }
        if (_path is null || goalMoved || stuck && now - _pathAt > 1500 || now - _pathAt > 4000)
        {
            _pathGoal = target;
            _pathAt = now;
            bool clear = _planner.Clear(nav, p, target);
            _path = clear ? [target] : _planner.Find(nav, p, target);
            // v0.3.9: jalur gagal/parsial → buang tanda tersangkut lama lalu cari sekali lagi.
            if (!clear && (_path is null || _planner.Partial) && _planner.ClearBlocks()) _path = _planner.Find(nav, p, target);
            // Diagnosa (maks 1x / 10 dtk): posisi, tujuan, jenis jalur, jumlah rintangan peta → bisa dicocokkan dengan snapshot.
            _pending.AddRange(Note("path-diag", now, 10000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Jalur ke {label}: dari ({p.X:0},{p.Z:0}) ke ({target.X:0},{target.Z:0}), {(clear ? "lurus" : _path is null ? "tidak ketemu" : $"{_path.Count} titik{(_planner.UsedOutside ? ", lewat luar Areas" : "")}{(_planner.Partial ? ", parsial" : "")}")}; peta {nav.Walkable.Count} area, {nav.Solids?.Count ?? 0} rintangan.")));
            if (_path is null || _planner.Partial)
                _pending.AddRange(Note("border-diag", now, 15000, $"Border/Blocker: {nav.BorderDiag ?? "?"}. HUD: \"{s.HudRaw ?? "-"}\"."));
            if (_path is null && OtherLevel(nav, p, target))
            {
                // v0.3.10 (ER, snapshot World 07:43:42): grid A* satu lantai. Spawn ER di lantai Y≈240, arena Areas.1 di Y≈278
                // lewat tangga (StairCollision miring); dari lantai bawah mesh tangga/silinder arena = "dinding" → jalur selalu gagal.
                // Tujuan beda lantai → jalan lurus seperti sebelum v0.3.9 (rute ER memang lurus naik tangga).
                return [.. WalkTo(s, target, radius, now, label, attackWhenStuck), .. Note("path-level", now, 8000, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Jalur ke {label}: tujuan beda lantai (kaki Y {PathPlanner.FootY(nav, p):0}, tujuan Y {target.Y:0}) → jalan lurus (naik tangga)."))];
            }
            if (_path is null)
            {
                // v0.3.9 (Byzan 25 Okt 10:46): dulu "Jalan lurus" = menabrak gedung yang sudah terbaca sebagai rintangan. Kini diam & cari ulang.
                Status = $"Jalur ke {label} tidak ketemu, cari ulang…";
                return [new MoveAction([], "Tunggu jalur"), .. Note("path-none", now, 8000, $"Jalur ke {label} tidak ketemu: {_planner.LastFailure ?? "?"}. Tidak jalan lurus menembus rintangan; cari ulang.")];
            }
            if (_planner.Partial) _pathNote = $"Jalur ke {label}: {_planner.LastFailure}.";
            else if (_planner.UsedOutside && _path.Count > 1) _pathNote = $"Jalur ke {label} memutar lewat jalan di luar kotak Areas ({_path.Count} titik belok).";
        }
        // Buang titik belok yang sudah dilewati (≤ 3 stud) atau yang sudah bisa dilihat titik sesudahnya.
        while (_path.Count > 1 && Navigator.Distance2D(p.X, p.Z, _path[0].X, _path[0].Z) <= 3) _path.RemoveAt(0);
        var wp = _path[0];
        bool last = _path.Count == 1;
        var walk = WalkTo(s, wp, last ? radius : 1.5, now, last ? label : $"{label} (belok {_path.Count - 1})", attackWhenStuck);
        // Jalur parsial sudah sampai ujung: sisanya jalan lurus (mis. hancurkan rubble/Blocker dengan serang saat tersangkut).
        // v0.3.9: ujung jalur parsial → tidak jalan lurus ke gedung/Blocker (rubble punya pendekatan sendiri); cari ulang.
        if (walk.Count == 0 && last && _planner.Partial && OtherLevel(nav, p, target)) return WalkTo(s, target, radius, now, label, attackWhenStuck);
        if (walk.Count == 0 && last && _planner.Partial) { _path = null; Status = $"Ujung jalur parsial ke {label}, cari ulang…"; return [new MoveAction([], "Tunggu jalur")]; }
        if (_pathNote is { } pn) { _pathNote = null; walk = [.. walk, .. Note("path-partial", now, 8000, pn)]; }
        if (walk.Count == 0 && !last) { _path.RemoveAt(0); return WalkPath(s, target, radius, now, label, attackWhenStuck); }
        if (walk.Count == 0) _path = null;
        return walk;
    }

    /// <summary>v0.3.10: tujuan ≥ 8 stud di atas/bawah lantai kaki (mis. arena ER di atas tangga spawn). Hxh: entry Y≈2, kaki Y≈3.</summary>
    internal static bool OtherLevel(NavMap nav, Vec3 p, Vec3 target) => Math.Abs(target.Y - PathPlanner.FootY(nav, p)) >= 8;

    private List<MacroAction> WalkTo(GameSnapshot s, Vec3 target, double radius, long now, string label, bool attackWhenStuck)
    {
        if (s.PlayerPos is not { } p || s.CameraLook is not { } look)
        {
            Status = "Posisi karakter/kamera tidak terbaca.";
            return [new MoveAction([], "Data posisi tidak ada"), .. Note("nopos", now, 6000, "Posisi karakter atau kamera tidak terbaca (cek offsets).")];
        }
        var keys = Navigator.KeysToward(p.X, p.Z, look.X, look.Z, target.X, target.Z, radius);
        if (keys.Count == 0) { _stuckRef = null; return []; }
        var actions = new List<MacroAction> { new MoveAction(keys, $"Jalan ke {label}") };
        if (_stuckRef is null || Navigator.Distance2D(p.X, p.Z, _stuckRef.X, _stuckRef.Z) > 1.5)
        {
            _stuckRef = p;
            _stuckSince = now;
        }
        else if (now - _stuckSince >= 1500)
        {
            if (Ready("jump", now, 1200)) actions.Add(new KeyAction(VirtualKeys.Space, 80, "Lompat (tersangkut)"));
            if (attackWhenStuck && now - _lastAttack >= 150)
            {
                _lastAttack = now;
                actions.Add(new AttackAction("Serang rintangan"));
            }
        }
        Status = $"Jalan ke {label} ({Navigator.Distance2D(p.X, p.Z, target.X, target.Z):0} stud)";
        return actions;
    }

    // ---------- Mode non-Story ----------
}

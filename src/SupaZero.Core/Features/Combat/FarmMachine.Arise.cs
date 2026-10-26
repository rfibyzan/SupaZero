using SupaZero.Core.Game;
using SupaZero.Core.Input;
using SupaZero.Core.Settings;

namespace SupaZero.Core.Automation;

public sealed partial class FarmMachine
{
    /// <summary>Radius cari mayat ARISE; jarak tahan E (prompt MaxActivationDistance 10); batas waktu per mayat.</summary>
    internal const double AriseRadius = 40, AriseHoldDist = 4;
    internal const long AriseGiveUpMs = 8000;
    private readonly Dictionary<string, long> _ariseBan = new(StringComparer.Ordinal);
    private string? _ariseKey;
    private long _ariseSince, _ariseLastHold;
    private int _ariseHolds;

    private int? _ariseShadowsBefore;
    private static string AriseKey(AriseInfo a) => $"{Math.Round(a.Pos.X)},{Math.Round(a.Pos.Z)}";

    /// <summary>Mayat ARISE terdekat: jalan ke sana; dalam 4 stud → lepas gerak + tahan E. null = tidak ada mayat.</summary>
    private List<MacroAction>? Arise(GameSnapshot s, long now, AppSettings S)
    {
        // v0.2.70: kapasitas Shadows penuh (mis. 3/3) → jangan buang waktu ke mayat.
        if (s.PlayerPos is not { } me || s.Arises.Count == 0 || s.Shadows is { } sh && sh.Count >= sh.Max) { _ariseKey = null; return null; }
        // v0.2.79 (jangan menebak): jumlah Shadows wajib terbaca, agar hasil Arise bisa diverifikasi (Count naik).
        if (s.Shadows is not { } shNow) { _ariseKey = null; _pending.AddRange(Note("arise-noshadow", now, 15000, "Mayat Arise ada, tapi jumlah Shadows tidak terbaca → Arise dilewati.")); return null; }
        if (_ariseKey is not null && _ariseShadowsBefore is int before && shNow.Count > before)
        {
            _ariseBan[_ariseKey] = now + 60_000;
            _pending.AddRange(Note("arise-ok", now, 1000, $"Arise berhasil: Shadows {before} → {shNow.Count}/{shNow.Max}."));
            _ariseKey = null; _ariseShadowsBefore = null;
            return null;
        }
        var best = s.Arises
            .Where(a => !(_ariseBan.TryGetValue(AriseKey(a), out long until) && until > now))
            .Select(a => (a, d: Navigator.Distance2D(me.X, me.Z, a.Pos.X, a.Pos.Z)))
            .Where(t => t.d <= AriseRadius && Math.Abs(t.a.Pos.Y - me.Y) <= 25)
            .OrderBy(t => t.d).FirstOrDefault();
        if (best.a is null) { _ariseKey = null; return null; }
        string key = AriseKey(best.a);
        if (_ariseKey != key) { _ariseKey = key; _ariseSince = now; _ariseHolds = 0; _ariseShadowsBefore = shNow.Count; }
        // Sudah 2× tahan E tapi prompt masih ada (mis. kapasitas Shadows penuh) → lewati 60 dtk.
        else if (now - _ariseSince > AriseGiveUpMs || (_ariseHolds >= 2 && now - _ariseLastHold >= 1500))
        {
            _ariseBan[key] = now + 60_000;
            _ariseKey = null;
            return [new MoveAction([], "Lewati Arise"), .. Note("arise-ban", now, 4000, $"Arise {best.a.Name} tidak berhasil dalam {AriseGiveUpMs / 1000} dtk, dilewati.")];
        }
        Status = $"Arise {best.a.Name} ({best.d:0} stud)";
        if (best.d > AriseHoldDist)
        {
            var walk = WalkPath(s, best.a.Pos, AriseHoldDist - 1, now, $"mayat {best.a.Name}", false);
            if (walk.Count > 0) return walk;
        }
        // Sudah dekat: tahan E sekali per 1,5 dtk; di sela itu lompat agar tidak diam.
        if (now - _ariseLastHold >= 1500)
        {
            _ariseLastHold = now;
            _ariseHolds++;
            int ms = (int)Math.Round(best.a.Hold * 1000) + 250;
            return [new MoveAction([], "Diam sebentar untuk Arise"), new KeyAction(VirtualKeys.E, ms, $"Arise {best.a.Name} (tahan E {ms} ms)")];
        }
        return [new MoveAction([], "Tunggu Arise"), new KeyAction(VirtualKeys.Space, 80, "Lompat (menunggu Arise)")];
    }

    // ---------- Cari jalur (A* di peta Areas + rintangan Navigation) ----------
}

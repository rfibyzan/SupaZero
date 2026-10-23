namespace SupaZero.Core.Automation;

/// <summary>
/// Jalan ke titik tujuan TANPA teleport dan tanpa tulis memori: posisi karakter + arah kamera dibaca read-only, lalu
/// dipilih kombinasi W/A/S/D (8 arah) yang paling dekat dengan arah tujuan. Gerak Roblox relatif kamera, jadi kamera
/// tidak perlu diputar.
/// </summary>
public static class Navigator
{
    private const double Threshold = 0.38; // ≈ sin 22.5°: batas sektor 8 arah

    /// <summary>
    /// Tombol yang harus ditahan. Kosong = sudah sampai (dalam <paramref name="arriveRadius"/>) atau data tidak valid.
    /// </summary>
    public static IReadOnlyList<ushort> KeysToward(double px, double pz, double lookX, double lookZ, double tx, double tz, double arriveRadius)
    {
        double dx = tx - px, dz = tz - pz;
        double dist = Math.Sqrt(dx * dx + dz * dz);
        double fl = Math.Sqrt(lookX * lookX + lookZ * lookZ);
        if (dist <= arriveRadius || fl < 1e-6 || !double.IsFinite(dist)) return [];
        dx /= dist; dz /= dist;
        double fx = lookX / fl, fz = lookZ / fl;
        double rx = -fz, rz = fx; // RightVector = Look × Up
        double forward = dx * fx + dz * fz;
        double right = dx * rx + dz * rz;
        var keys = new List<ushort>(2);
        if (forward > Threshold) keys.Add(VirtualKeys.W);
        else if (forward < -Threshold) keys.Add(VirtualKeys.S);
        if (right > Threshold) keys.Add(VirtualKeys.D);
        else if (right < -Threshold) keys.Add(VirtualKeys.A);
        return keys;
    }

    public static double Distance2D(double ax, double az, double bx, double bz) =>
        Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}

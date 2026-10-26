namespace SupaZero.Core.Automation;

/// <summary>
/// Walk to a destination point WITHOUT teleporting and without writing memory: character position + camera direction are read read-only, then
/// the W/A/S/D combination (8 directions) closest to the target direction is chosen. Roblox movement is camera-relative, so the camera
/// does not need to be turned.
/// </summary>
public static class Navigator
{
    private const double Threshold = 0.38; // ≈ sin 22.5°: 8-direction sector boundary

    /// <summary>
    /// Keys to hold. Empty = arrived (within <paramref name="arriveRadius"/>) or invalid data.
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

using RIE.Core.Memory;
using RIE.Core.Offsets;

namespace RIE.Core.Macro;

/// <summary>VisualEngine view-projection matrix + render size (client pixels).</summary>
public sealed record ViewState(float[] Matrix, float Width, float Height)
{
    public bool IsValid => Matrix.Length == 16 && Width > 1 && Height > 1;
}

/// <summary>
/// Projects a world position (studs) to Roblox window client coordinates using VisualEngine.ViewMatrix (row-major 4x4).
/// The result already includes the topbar (3D rendering fills the client area), so no Y inset is needed.
/// Read-only: only reads [moduleBase + VisualEngine.Pointer] → ViewMatrix and Dimensions.
/// </summary>
public static class WorldToScreen
{
    public static bool Project(ViewState view, double x, double y, double z, out double sx, out double sy)
    {
        sx = sy = 0;
        if (!view.IsValid) return false;
        var m = view.Matrix;
        double cx = m[0] * x + m[1] * y + m[2] * z + m[3];
        double cy = m[4] * x + m[5] * y + m[6] * z + m[7];
        double w = m[12] * x + m[13] * y + m[14] * z + m[15];
        if (w < 0.1) return false; // behind the camera
        double nx = cx / w, ny = cy / w;
        sx = view.Width / 2.0 * nx + view.Width / 2.0;
        sy = -(view.Height / 2.0) * ny + view.Height / 2.0;
        return double.IsFinite(sx) && double.IsFinite(sy);
    }

    public static bool IsOnScreen(ViewState view, double sx, double sy) => sx >= 0 && sy >= 0 && sx <= view.Width && sy <= view.Height;

    public static ViewState? ReadView(IMemorySource mem, OffsetsSnapshot offsets, long moduleBase)
    {
        if (moduleBase == 0) return null;
        if (!offsets.TryResolve("VisualEngine.Pointer", out long ptrOff) || !offsets.TryResolve("VisualEngine.ViewMatrix", out long vmOff)) return null;
        if (!mem.TryReadPointer(moduleBase + ptrOff, out long ve)) return null;
        var m = new float[16];
        for (int i = 0; i < 16; i++)
            if (!mem.TryReadFloat(ve + vmOff + i * 4, out m[i]) || !float.IsFinite(m[i])) return null;
        float w = 0, h = 0;
        if (offsets.TryResolve("VisualEngine.Dimensions", out long dOff))
        {
            mem.TryReadFloat(ve + dOff, out w);
            mem.TryReadFloat(ve + dOff + 4, out h);
        }
        var v = new ViewState(m, w, h);
        return v.IsValid ? v : null;
    }
}

namespace SupaZero.Core.Automation;

/// <summary>Sends input to Roblox. Real implementation = SendInput (Win32Input); tests use a fake recorder.</summary>
public interface IInput
{
    /// <summary>True when the Roblox window is in the foreground (input is only sent while this is true).</summary>
    bool IsTargetForeground();

    /// <summary>Left-click at Roblox window client coordinates.</summary>
    void ClickClient(double x, double y);

    /// <summary>Move the cursor to client coordinates, then scroll the mouse wheel. Notches &gt; 0 = up.</summary>
    void ScrollClient(double x, double y, int notches);

    /// <summary>Roblox window client area size (pixels), null when unknown.</summary>
    (int W, int H)? ClientSize();

    /// <summary>Move the cursor to client coordinates (no click).</summary>
    void MoveClient(double x, double y);

    /// <summary>Relative mouse movement (pixel delta), to turn the camera during shift lock.</summary>
    void MoveRelative(int dx, int dy);

    /// <summary>Cursor position in Roblox client coordinates, null when unknown.</summary>
    (double X, double Y)? CursorClient();

    /// <summary>Left-click at the current cursor position.</summary>
    void ClickHere();

    /// <summary>Hold / release right-click at the current cursor position.</summary>
    void RightDown();
    void RightUp();

    /// <summary>Type one character as Unicode (KEYEVENTF_UNICODE, no Shift).</summary>
    void TypeUnicode(char c);

    void KeyDown(ushort vk);
    void KeyUp(ushort vk);
}

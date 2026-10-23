using Avalonia.Input;

namespace SupaZero.App.Services;

/// <summary>
/// Key Avalonia → nama tombol HotkeyMap (tanpa Enum.ToString/refleksi). null = tombol tidak didukung untuk hotkey.
/// </summary>
public static class KeyNames
{
    public static string? From(Key key)
    {
        if (key >= Key.F1 && key <= Key.F24) return "F" + (key - Key.F1 + 1);
        if (key >= Key.A && key <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "NumPad" + (key - Key.NumPad0);
        return key switch
        {
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Pause => "Pause",
            Key.Scroll => "ScrollLock",
            Key.LeftAlt => "LeftAlt",
            Key.RightAlt => "RightAlt",
            Key.LeftCtrl => "LeftCtrl",
            Key.RightCtrl => "RightCtrl",
            Key.LeftShift => "LeftShift",
            Key.RightShift => "RightShift",
            _ => null,
        };
    }
}

namespace SupaZero.Core.Automation;

/// <summary>Input action decided by <see cref="StoryMachine"/>; executed by <see cref="MacroRunner"/> via <see cref="IInput"/>.</summary>
public abstract record MacroAction(string Reason);

/// <summary>Left-click at Roblox window CLIENT coordinates.</summary>
public sealed record ClickAction(double X, double Y, string Reason) : MacroAction(Reason)
{
    /// <summary>GUI click inside a stage (Vote Yes / Retry / Next): shift lock may be on even if not yet reliably detected.</summary>
    public bool InRound { get; init; }
}

/// <summary>Mouse wheel scroll at CLIENT coordinates (cursor is moved there first). Notches &gt; 0 = up, &lt; 0 = down.</summary>
public sealed record ScrollAction(double X, double Y, int Notches, string Reason) : MacroAction(Reason);

/// <summary>Move the cursor (no click) to CLIENT coordinates, e.g. aiming at an enemy.</summary>
public sealed record AimAction(double X, double Y, string Reason) : MacroAction(Reason);

/// <summary>Relative mouse movement (turn the camera during shift lock).</summary>
/// <summary>Drag = hold right-click while moving (turn the camera without shift lock).</summary>
public sealed record TurnAction(int Dx, int Dy, string Reason, bool Drag = false) : MacroAction(Reason);

/// <summary>Left-click at the current cursor position (basic attack).</summary>
public sealed record AttackAction(string Reason) : MacroAction(Reason);

/// <summary>Press a key (virtual-key) for <paramref name="HoldMs"/>.</summary>
public sealed record KeyAction(ushort VirtualKey, int HoldMs, string Reason) : MacroAction(Reason);

/// <summary>Hold exactly these movement keys (W/A/S/D); other movement keys are released. Empty = release all.</summary>
public sealed record MoveAction(IReadOnlyList<ushort> Keys, string Reason) : MacroAction(Reason);

/// <summary>No input; log note only (e.g. waiting for the player, feature not available yet).</summary>
public sealed record NoteAction(string Reason) : MacroAction(Reason);

/// <summary>Stop the macro (Story finished, retries exhausted, etc.).</summary>
public sealed record StopAction(string Reason) : MacroAction(Reason);

public static class VirtualKeys
{
    public const ushort W = 0x57, A = 0x41, S = 0x53, D = 0x44, Q = 0x51, T = 0x54, E = 0x45, Space = 0x20;

    /// <summary>"1" → 0x31, "Q" → 0x51. Single letters/digits only.</summary>
    public static bool TryParse(string? key, out ushort vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(key)) return false;
        string k = key.Trim().ToUpperInvariant();
        if (k.Length == 1 && (char.IsAsciiLetterUpper(k[0]) || char.IsAsciiDigit(k[0]))) { vk = k[0]; return true; }
        return false;
    }
}

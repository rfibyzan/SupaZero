namespace RIE.Core.Util;

/// <summary>Class category for coloured badges in the UI.</summary>
public enum ClassCategory { Other, Service, Gui, Part, Model, Script, Value, Humanoid, Effect, Player }

public static class UiText
{
    /// <summary>
    /// Truncates long text in the middle, e.g. "game.Players.Byzan…Main.Frame.Label".
    /// The tail (instance name) gets a larger share because it matters most.
    /// </summary>
    public static string MiddleEllipsis(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (max < 5 || s.Length <= max) return s;
        int keep = max - 1;              // 1 character for "…"
        int head = keep * 2 / 5;         // first 40%
        int tail = keep - head;          // last 60%
        return string.Concat(s.AsSpan(0, head), "…", s.AsSpan(s.Length - tail));
    }

    private static readonly HashSet<string> Services = new(StringComparer.Ordinal)
    {
        "DataModel", "Workspace", "Players", "ReplicatedStorage", "ReplicatedFirst", "StarterGui", "StarterPack",
        "StarterPlayer", "Lighting", "SoundService", "Teams", "CoreGui", "RunService", "UserInputService",
        "MouseService", "TweenService", "HttpService", "Chat", "TextChatService", "ServerStorage", "ServerScriptService",
        "MaterialService", "LocalizationService", "GuiService", "ContextActionService", "CollectionService",
    };

    public static ClassCategory Categorize(string? cls)
    {
        if (string.IsNullOrEmpty(cls)) return ClassCategory.Other;
        if (Services.Contains(cls) || cls.EndsWith("Service", StringComparison.Ordinal)) return ClassCategory.Service;
        if (cls is "Player" or "PlayerGui" or "Backpack" or "PlayerScripts") return ClassCategory.Player;
        if (cls is "Humanoid" or "Animator" or "AnimationTrack" or "Animation" or "HumanoidDescription") return ClassCategory.Humanoid;
        if (cls.EndsWith("Script", StringComparison.Ordinal) || cls is "ModuleScript" or "RemoteEvent" or "RemoteFunction" or "BindableEvent" or "BindableFunction" or "UnreliableRemoteEvent")
            return ClassCategory.Script;
        if (cls.EndsWith("Value", StringComparison.Ordinal) || cls is "Configuration") return ClassCategory.Value;
        if (cls.StartsWith("UI", StringComparison.Ordinal) || cls.EndsWith("Gui", StringComparison.Ordinal) ||
            cls is "Frame" or "TextLabel" or "TextButton" or "TextBox" or "ImageLabel" or "ImageButton" or "ScrollingFrame" or "ViewportFrame" or "CanvasGroup" or "VideoFrame")
            return ClassCategory.Gui;
        if (cls is "Model" or "Folder" or "Tool" or "Accessory" or "Camera" or "Terrain" or "WorldModel") return ClassCategory.Model;
        if (cls.EndsWith("Part", StringComparison.Ordinal) || cls is "MeshPart" or "UnionOperation" or "TrussPart" or "Seat" or "VehicleSeat" or "SpawnLocation" or "WedgePart" or "CornerWedgePart")
            return ClassCategory.Part;
        if (cls is "ProximityPrompt" or "ClickDetector" or "Sound" or "Decal" or "Texture" or "Attachment" or "Beam" or "Trail" or "Highlight" or "SelectionBox"
            || cls.EndsWith("Emitter", StringComparison.Ordinal) || cls.EndsWith("Light", StringComparison.Ordinal) || cls.EndsWith("Constraint", StringComparison.Ordinal) || cls.EndsWith("Weld", StringComparison.Ordinal))
            return ClassCategory.Effect;
        return ClassCategory.Other;
    }
}

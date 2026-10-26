namespace RIE.Core.Properties;

/// <summary>Short description (English) per property, for UI tooltips.</summary>
public static class PropertyDocs
{
    private static readonly Dictionary<string, string> Docs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Exists"] = "Whether an instance exists at this path right now (true/false). Good for appear/disappear triggers.",
        ["ClassName"] = "Roblox instance type, e.g. Frame, TextLabel, Part, Humanoid.",
        ["ChildCount"] = "Number of direct children. Changes when elements are created or removed under it.",
        ["Visible"] = "GUI shown (true) or hidden (false). A hidden parent still hides its children.",
        ["Enabled"] = "ScreenGui enabled (true) or disabled (false). If false, none of its contents are shown.",
        ["AbsolutePosition"] = "Top-left corner of the element in Roblox screen pixels (x, y), before Y inset and Scale.",
        ["AbsoluteSize"] = "Element size in pixels (width, height).",
        ["Text"] = "Text shown in the TextLabel/TextButton/TextBox.",
        ["Image"] = "Image asset (rbxassetid://...). Changes when the icon/image changes.",
        ["Transparency"] = "Part transparency: 0 = solid, 1 = invisible.",
        ["Health"] = "Current Humanoid health. 0 = dead.",
        ["MaxHealth"] = "Maximum Humanoid health.",
        ["WalkSpeed"] = "Walk speed (studs/second). Default 16.",
        ["JumpPower"] = "Jump power. Default 50.",
        ["HipHeight"] = "Hip height above the ground (studs).",
        ["MoveDirection"] = "Character move direction (unit vector). 0, 0, 0 = idle.",
        ["Jump"] = "true briefly when the character is told to jump.",
        ["Sit"] = "true while the character is sitting.",
        ["IsWalking"] = "true while the character is walking.",
        ["SeatPart"] = "Seat currently sat in, or nil.",
        ["StateId"] = "Humanoid state (number and name), e.g. 8 Running, 3 Jumping, 5 Freefall, 15 Dead.",
        ["FloorMaterial"] = "Material code of the floor being stood on (Enum.Material).",
        ["LookVector"] = "Camera look direction (unit vector).",
        ["FieldOfView"] = "Camera field of view in degrees. Default 70.",
        ["ViewportSize"] = "Camera render area size (pixels).",
        ["CameraSubject"] = "Instance the camera follows, usually the player's Humanoid.",
        ["UserId"] = "Player's Roblox account ID.",
        ["DisplayName"] = "Player's display name.",
        ["Character"] = "Player's character model in Workspace, or nil before spawning.",
        ["Team"] = "Player's team, or nil.",
        ["PrimaryPart"] = "Model's primary part, used as its position reference.",
        ["PlaceId"] = "ID of the place (server/game) being played.",
        ["GameId"] = "Game universe ID.",
        ["Value"] = "ValueBase contents (IntValue, NumberValue, BoolValue, StringValue, ObjectValue, Vector3Value). Often used by games to store state.",
        ["TextColor3"] = "Text colour (R, G, B 0..255). Useful to detect text that changes colour, e.g. red on failure.",
        ["BackgroundColor3"] = "GUI element background colour (R, G, B 0..255). Bars/indicators often change colour.",
        ["BackgroundTransparency"] = "GUI background transparency: 0 = solid, 1 = invisible.",
        ["ZIndex"] = "GUI stacking order; higher numbers render on top.",
        ["LayoutOrder"] = "Element order inside a UIListLayout/UIGridLayout.",
        ["Rotation"] = "GUI element rotation in degrees.",
        ["Color"] = "Part colour (R, G, B 0..255).",
        ["Velocity"] = "Part velocity (studs/second, x, y, z). 0 = stationary.",
        ["Anchored"] = "Part is locked in place (not affected by physics).",
        ["CanCollide"] = "Part can be collided with/blocks characters.",
        ["CanTouch"] = "Part fires the Touched event.",
        ["Occupant"] = "Humanoid currently sitting in this seat, or nil.",
        ["ThrottleFloat"] = "Vehicle throttle: -1 reverse, 0 idle, 1 forward.",
        ["SteerFloat"] = "Vehicle steering: -1 left, 0 straight, 1 right.",
        ["PlatformStand"] = "true when the Humanoid cannot move on its own (flung, ragdoll).",
        ["AutoRotate"] = "Character automatically faces the move direction.",
        ["JumpHeight"] = "Jump height (studs), used when UseJumpPower is false.",
        ["MoveToPoint"] = "Target point of a MoveTo / click-to-move command.",
        ["RootPart"] = "Character's HumanoidRootPart, the player position reference.",
        ["RigType"] = "Character rig type: R6 or R15.",
        ["ActionText"] = "ProximityPrompt action text, e.g. \"Fish\" or \"Open\".",
        ["ObjectText"] = "Object name in the ProximityPrompt, e.g. \"Chest\".",
        ["HoldDuration"] = "How long the key must be held (seconds). 0 = a single press.",
        ["MaxActivationDistance"] = "Maximum distance (studs) for the prompt to appear.",
        ["KeyCode"] = "Keyboard key for the prompt (code and name), usually E.",
        ["RequiresLineOfSight"] = "Prompt only appears when the object is in direct line of sight.",
        ["Debug ActiveAnimations"] = "Animation list read diagnostics: head address, node count, skipped nodes, valid tracks. Send this value if ActiveTracks is 0.",
        ["ActiveTracks"] = "Number of animations currently active on this Animator.",
        ["Speed"] = "Animation playback speed (1 = normal).",
        ["TimePosition"] = "Animation time position (seconds). Good for action timing.",
        ["Looped"] = "Plays looped (true) or once (false).",
        ["IsPlaying"] = "true while the animation/sound is playing.",
        ["AnimationId"] = "Animation asset (rbxassetid://...). Each game action usually has its own ID.",
        ["SoundId"] = "Sound asset (rbxassetid://...). Sound effects can serve as macro signals.",
        ["Volume"] = "Sound volume.",
        ["PlaybackSpeed"] = "Sound playback speed (1 = normal).",
        ["ToolTip"] = "Tool tooltip text in the hotbar.",
        ["DistributedGameTime"] = "Game time elapsed (seconds) since the server started.",
        ["CurrentCamera"] = "Workspace's active camera.",
        ["ClockTime"] = "Time of day in the game world (0..24).",
        ["MousePosition"] = "Cursor position according to Roblox (pixels). Used to calibrate Y inset/Scale.",
        ["Screen 3D client"] = "Position of the 3D object in the Roblox window client area (pixels), from camera projection. Does not use the Y inset.",
        ["Screen 3D absolute"] = "Position of the 3D object on the monitor. Click point for macros.",
        ["Screen 3D visible"] = "true if the object is in front of the camera and inside the window.",
        ["Screen client center"] = "Element center in Roblox window client-area coordinates, with Y inset and Scale applied.",
        ["Screen absolute center"] = "Element center in monitor screen coordinates. This is the click point for macros.",
        ["Screen normalized"] = "Element center as a 0..1 fraction of client width/height. Robust to resolution changes.",
        ["Client window"] = "Position and size of the Roblox window client area, plus the Windows DPI scale.",
    };

    /// <summary>Property description; <paramref name="className"/> distinguishes the meaning of Position/Size.</summary>
    public static string Describe(string property, string? className = null)
    {
        string p = property.Trim();
        if (p is "Position" or "Size")
        {
            bool gui = className is not null && (PropertyReader.IsGuiObject(className));
            if (gui)
                return p == "Position"
                    ? "UDim2 position {scaleX, offsetX}, {scaleY, offsetY} relative to the parent."
                    : "UDim2 size {scaleX, offsetX}, {scaleY, offsetY} relative to the parent.";
            if (className == "Camera" && p == "Position") return "Camera position in the world (x, y, z).";
            return p == "Position" ? "World position in studs (x, y, z)." : "Size in studs (x, y, z).";
        }
        if (p.StartsWith("Track", StringComparison.Ordinal) && p.Length > 5 && char.IsDigit(p[5]))
            return "Active animation: AnimationId | playing/stopped state | time position (seconds).";
        return Docs.TryGetValue(p, out var d) ? d : string.Empty;
    }

    public static bool Has(string property) => property is "Position" or "Size" || Docs.ContainsKey(property);
}

namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    /// <summary>v0.3.26: Codes button (lobby), Codes menu (TextBox / Redeem / Exit) and the result toasts. Only shown = visible elements.</summary>
    private GameSnapshot ReadCodes(GameSnapshot snap, long pg)
    {
        if (pg == 0) return snap;
        long btn = reader.ResolvePath(pg, AnimeZeroPaths.CodesButton);
        long gui = reader.ResolvePath(pg, AnimeZeroPaths.CodesGui);
        long frame = gui != 0 ? reader.ResolvePath(pg, AnimeZeroPaths.CodesFrame) : 0;
        bool open = frame != 0 && IsShown(gui, pg) && IsShown(frame, pg);
        GuiRect? R(string rel)
        {
            if (!open) return null;
            long n = reader.ResolvePath(frame, rel);
            return n != 0 && IsShown(n, pg) && HubRect(n) is { W: > 4, H: > 4 } r ? r with { Y = r.Y + SurfaceShiftY(frame) * GuiScale } : null;
        }
        long box = open ? reader.ResolvePath(frame, AnimeZeroPaths.CodesTextBox) : 0;
        return snap with
        {
            CodesButton = btn != 0 && IsShown(btn, pg) && HubRect(btn) is { W: > 4, H: > 4 } br ? br : null,
            CodesOpen = open,
            CodesTextBox = R(AnimeZeroPaths.CodesTextBox),
            CodesText = box != 0 ? Text(box) ?? "" : null,
            CodesRedeem = R(AnimeZeroPaths.CodesRedeem),
            CodesExit = R(AnimeZeroPaths.CodesExit),
            Notifications = ReadNotifications(pg),
        };
    }

    private List<NotificationInfo> ReadNotifications(long pg)
    {
        var list = new List<NotificationInfo>();
        long root = reader.ResolvePath(pg, AnimeZeroPaths.Notifications);
        if (root == 0 || !IsShown(root, pg)) return list;
        foreach (long n in reader.GetChildren(root, 64))
        {
            if (reader.GetName(n) != "Notification" || !IsShown(n, pg)) continue;
            var parts = reader.GetChildren(n, 32).Where(c => reader.GetClassName(c) == "TextLabel").Select(c => CleanText(c)).Where(t => t is not null);
            string text = string.Join(" ", parts).Trim();
            if (text.Length > 0) list.Add(new NotificationInfo(n, text));
        }
        return list;
    }
}

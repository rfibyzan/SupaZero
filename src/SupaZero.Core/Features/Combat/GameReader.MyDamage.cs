namespace SupaZero.Core.Game;

public sealed partial class GameReader
{
    /// <summary>v0.3.23 Carry: PlayerGui.RunPlayerList...Rows.&lt;LocalPlayer.UserId&gt;.Content.Info.Damage.Text ("5,712" / "11.3K").</summary>
    private long? ReadMyDamage(long pg, long localPlayer)
    {
        if (pg == 0 || localPlayer == 0) return null;
        string? uid = Prop(localPlayer, "UserId");
        if (string.IsNullOrEmpty(uid)) return null;
        long rows = reader.ResolvePath(pg, AnimeZeroPaths.RunPlayerRows);
        long row = rows != 0 ? reader.FindChild(rows, uid) : 0;
        long txt = row != 0 ? reader.ResolvePath(row, AnimeZeroPaths.RunPlayerRowDamage) : 0;
        return txt != 0 ? ParseDamage(Text(txt)) : null;
    }

    public static long? ParseDamage(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(t.Trim(), @"^([\d.,]+)\s*([KkMmBb]?)$");
        if (!m.Success) return null;
        string num = m.Groups[1].Value;
        string suf = m.Groups[2].Value.ToUpperInvariant();
        if (suf.Length == 0) num = num.Replace(",", "").Replace(".", "");
        else num = num.Replace(",", ".");
        if (!double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return null;
        v *= suf switch { "K" => 1e3, "M" => 1e6, "B" => 1e9, _ => 1 };
        return (long)Math.Round(v);
    }
}

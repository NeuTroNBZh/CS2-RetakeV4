using System.Net;

namespace RetakeV4.Domain.Hud;

// Color: optional tint (team entries); the highlighted line always uses the accent color.
public sealed record CenterMenuRow(string Text, bool Selected, string? Color = null);

// A menu drawn as an HTML panel in the center of the screen: title, numbered rows, highlighted cursor, optional notice, key hint.
public static class CenterMenuHtml
{
    public const int DefaultMaxVisible = 6;

    private const string Cursor = "&#9654; ";
    private const string MoreAbove = "&#9650;";
    private const string MoreBelow = "&#9660;";

    // Lists longer than maxVisible scroll: a window centered on the cursor, arrows when lines are hidden above or below.
    public static string Format(string title, IReadOnlyList<CenterMenuRow> rows, string hint, HudTheme theme, string? notice = null,
        int maxVisible = DefaultMaxVisible)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVisible, 1);
        var header = $"<font class='fontSize-xl' color='{theme.Accent}'><b>{WebUtility.HtmlEncode(title)}</b></font>";
        var start = WindowStart(rows, maxVisible);
        var visible = rows.Skip(start).Take(maxVisible).Select((row, offset) =>
            $"<font class='fontSize-m' color='{(row.Selected ? theme.Accent : row.Color ?? theme.Text)}'>{(row.Selected ? Cursor : string.Empty)}{start + offset + 1}. {WebUtility.HtmlEncode(row.Text)}</font>");
        var arrow = (string symbol) => $"<font class='fontSize-s' color='{theme.Muted}'>{symbol}</font>";
        var body = (start > 0 ? new[] { arrow(MoreAbove) } : Array.Empty<string>())
            .Concat(visible)
            .Concat(start + maxVisible < rows.Count ? new[] { arrow(MoreBelow) } : Array.Empty<string>());
        var footer = $"<font class='fontSize-s' color='{theme.Muted}'>{WebUtility.HtmlEncode(hint)}</font>";
        var tail = notice is null
            ? new[] { footer }
            : new[] { $"<font class='fontSize-m' color='{theme.Accent}'>{WebUtility.HtmlEncode(notice)}</font>", footer };
        return string.Join("<br>", body.Prepend(header).Concat(tail));
    }

    private static int WindowStart(IReadOnlyList<CenterMenuRow> rows, int maxVisible)
    {
        if (rows.Count <= maxVisible)
        {
            return 0;
        }
        var selected = Math.Max(0, rows.ToList().FindIndex(r => r.Selected));
        return Math.Clamp(selected - maxVisible / 2, 0, rows.Count - maxVisible);
    }
}

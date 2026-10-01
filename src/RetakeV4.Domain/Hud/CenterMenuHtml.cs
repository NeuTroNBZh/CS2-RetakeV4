using System.Net;

namespace RetakeV4.Domain.Hud;

// Color: optional tint (team entries); the highlighted line always uses the accent color.
public sealed record CenterMenuRow(string Text, bool Selected, string? Color = null);

// A menu drawn as an HTML panel in the center of the screen: title, numbered rows (number keys), highlighted cursor, key hint.
public static class CenterMenuHtml
{
    private const string Cursor = "&#9654; ";

    public static string Format(string title, IReadOnlyList<CenterMenuRow> rows, string hint, HudTheme theme)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(theme);
        var header = $"<font class='fontSize-xl' color='{theme.Accent}'><b>{WebUtility.HtmlEncode(title)}</b></font>";
        var lines = rows.Select((row, index) =>
            $"<font class='fontSize-m' color='{(row.Selected ? theme.Accent : row.Color ?? theme.Text)}'>{(row.Selected ? Cursor : string.Empty)}{index + 1}. {WebUtility.HtmlEncode(row.Text)}</font>");
        var footer = $"<font class='fontSize-s' color='{theme.Muted}'>{WebUtility.HtmlEncode(hint)}</font>";
        return string.Join("<br>", lines.Prepend(header).Append(footer));
    }
}

using System.Net;

namespace RetakeV4.Domain.Hud;

public static class CenterHtml
{
    // Texts come from lang files and config (round type names): always escaped. Colors are validated #RRGGBB values.
    public static string? Format(IReadOnlyList<(string Text, HudStyle Style)> lines, HudTheme theme) =>
        lines.Count == 0
            ? null
            : string.Join("<br>", lines.Select(l => $"<font color='{theme.ColorOf(l.Style)}'>{WebUtility.HtmlEncode(l.Text)}</font>"));
}

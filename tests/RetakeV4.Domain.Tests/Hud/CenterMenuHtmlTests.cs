using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Hud;

public class CenterMenuHtmlTests
{
    private static readonly HudTheme Theme = new("#4FC3F7", "#FFFFFF", "#9E9E9E");

    private static string Format(params CenterMenuRow[] rows) => CenterMenuHtml.Format("Weapons", rows, "W/S - E", Theme);

    [Fact]
    public void Title_IsBoldInTheAccentColor() =>
        Assert.StartsWith("<font class='fontSize-l' color='#4FC3F7'><b>Weapons</b></font>", Format(new CenterMenuRow("AK-47", false)));

    [Fact]
    public void Rows_AreNumbered_ForTheNumberKeys()
    {
        var html = Format(new CenterMenuRow("AK-47", false), new CenterMenuRow("M4A4", false));
        Assert.Contains("1. AK-47", html, StringComparison.Ordinal);
        Assert.Contains("2. M4A4", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedRow_IsHighlighted_OthersAreNot()
    {
        var html = Format(new CenterMenuRow("AK-47", true), new CenterMenuRow("M4A4", false));
        Assert.Contains("<font class='fontSize-m' color='#4FC3F7'>&#9654; 1. AK-47</font>", html, StringComparison.Ordinal);
        Assert.Contains("<font class='fontSize-m' color='#FFFFFF'>2. M4A4</font>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Hint_EndsThePanel_InTheMutedColor() =>
        Assert.EndsWith("<font class='fontSize-s' color='#9E9E9E'>W/S - E</font>", Format(new CenterMenuRow("AK-47", false)));

    // Labels come from lang files and round type names: never trusted as HTML.
    [Fact]
    public void Texts_AreEscaped()
    {
        var html = CenterMenuHtml.Format("<b>x</b>", new[] { new CenterMenuRow("<script>", false) }, "a&b", Theme);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("a&amp;b", html, StringComparison.Ordinal);
    }
}

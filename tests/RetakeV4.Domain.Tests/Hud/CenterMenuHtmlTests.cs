using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Hud;

public class CenterMenuHtmlTests
{
    private static readonly HudTheme Theme = new("#4FC3F7", "#FFFFFF", "#9E9E9E");

    private static string Format(params CenterMenuRow[] rows) => CenterMenuHtml.Format("Weapons", rows, "W/S - E", Theme);

    [Fact]
    public void Title_IsBoldInTheAccentColor() =>
        Assert.StartsWith("<font class='fontSize-xl' color='#4FC3F7'><b>Weapons</b></font>", Format(new CenterMenuRow("AK-47", false)));

    [Fact]
    public void Rows_AreNumbered()
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

    // Team entries keep their team color unless they are the highlighted line.
    [Fact]
    public void TintedRow_UsesItsColor_UnlessSelected()
    {
        var html = Format(new CenterMenuRow("Terrorists", false, "#EAB54F"), new CenterMenuRow("Counter-Terrorists", true, "#5D9CEC"));
        Assert.Contains("<font class='fontSize-m' color='#EAB54F'>1. Terrorists</font>", html, StringComparison.Ordinal);
        Assert.Contains("color='#4FC3F7'>&#9654; 2. Counter-Terrorists", html, StringComparison.Ordinal);
    }

    // A confirmation published while the menu covers the info block ("applied next round") shows above the hint.
    [Fact]
    public void Notice_IsShownAboveTheHint()
    {
        var html = CenterMenuHtml.Format("Weapons", new[] { new CenterMenuRow("AK-47", false) }, "hint", Theme, "Saved <now>");
        Assert.EndsWith("<font class='fontSize-m' color='#4FC3F7'>Saved &lt;now&gt;</font><br><font class='fontSize-s' color='#9E9E9E'>hint</font>", html);
    }

    private static CenterMenuRow[] Rows(int count, int selected) =>
        Enumerable.Range(1, count).Select(i => new CenterMenuRow($"w{i}", i - 1 == selected)).ToArray();

    // A long list (pistols) shows a window that follows the cursor, with arrows when more lines are hidden.
    [Fact]
    public void LongList_ShowsAWindowAroundTheCursor_WithScrollArrows()
    {
        var html = CenterMenuHtml.Format("Pistols", Rows(10, 5), "hint", Theme, maxVisible: 4);
        Assert.Contains("&#9650;", html, StringComparison.Ordinal);
        Assert.Contains("&#9660;", html, StringComparison.Ordinal);
        Assert.Contains("6. w6", html, StringComparison.Ordinal);
        Assert.DoesNotContain("1. w1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("10. w10", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_AtTheTop_HasNoUpArrow()
    {
        var html = CenterMenuHtml.Format("Pistols", Rows(10, 0), "hint", Theme, maxVisible: 4);
        Assert.DoesNotContain("&#9650;", html, StringComparison.Ordinal);
        Assert.Contains("4. w4", html, StringComparison.Ordinal);
        Assert.DoesNotContain("5. w5", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_AtTheBottom_HasNoDownArrow()
    {
        var html = CenterMenuHtml.Format("Pistols", Rows(10, 9), "hint", Theme, maxVisible: 4);
        Assert.DoesNotContain("&#9660;", html, StringComparison.Ordinal);
        Assert.Contains("7. w7", html, StringComparison.Ordinal);
        Assert.Contains("10. w10", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortList_ShowsEverything_WithoutArrows()
    {
        var html = CenterMenuHtml.Format("Weapons", Rows(3, 1), "hint", Theme, maxVisible: 4);
        Assert.DoesNotContain("&#9650;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&#9660;", html, StringComparison.Ordinal);
    }
}

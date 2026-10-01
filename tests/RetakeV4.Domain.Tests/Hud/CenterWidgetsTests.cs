using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Hud;

public class CenterWidgetsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId Alice = new(1);
    private static readonly PlayerId Bob = new(2);
    private static readonly HudTheme Theme = new("#4FC3F7", "#FFFFFF", "#9E9E9E");

    [Fact]
    public void RoundInfo_IsShownForItsDuration()
    {
        var widget = RoundInfoWidget.Empty.Show(new RoundInfo("FullBuy", BombSite.B, 4, 3), Now, TimeSpan.FromSeconds(6));
        var lines = widget.Render(Now.AddSeconds(5));
        Assert.Equal(new object[] { "FullBuy", "B" }, lines[0].Text.Args);
        Assert.Equal(HudStyle.Accent, lines[0].Style);
        Assert.Equal(new object[] { 4, 3 }, lines[1].Text.Args);
        Assert.Equal(2, lines.Count);
        Assert.Empty(widget.Render(Now.AddSeconds(6)));
        Assert.Empty(RoundInfoWidget.Empty.Render(Now));
    }

    [Fact]
    public void RoundInfo_ShowsTheStreak_AndAnUnknownSite()
    {
        var widget = RoundInfoWidget.Empty.WithStreak(2).Show(new RoundInfo("Pistol", null, 1, 1), Now, TimeSpan.FromSeconds(6));
        var lines = widget.Render(Now);
        Assert.Equal("?", lines[0].Text.Args[1]);
        Assert.Equal("hud.round.streak", lines[2].Text.Key);
        Assert.Equal(HudStyle.Muted, lines[2].Style);
    }

    [Fact]
    public void QueueStatus_ShowsThePositionAndPriority()
    {
        var state = TeamState.Empty with
        {
            Queue = new[] { new QueuedPlayer(Alice, 0, 1), new QueuedPlayer(Bob, 1, 2) },
        };
        var widget = QueueStatusWidget.From(state);
        var bob = widget.Render(Bob);
        Assert.Equal(new object[] { 1, 2 }, bob[0].Text.Args);
        Assert.Equal("hud.queue.priority", bob[1].Text.Key);
        var alice = Assert.Single(widget.Render(Alice));
        Assert.Equal(new object[] { 2, 2 }, alice.Text.Args);
        Assert.Empty(widget.Render(new PlayerId(9)));
    }

    [Fact]
    public void Alerts_AreTargeted_Expire_AndKeepTheNewest()
    {
        var widget = AlertsWidget.Empty
            .Push(Alice, HudText.Raw("a1"), Now, TimeSpan.FromSeconds(4))
            .Push(null, HudText.Raw("all"), Now, TimeSpan.FromSeconds(4))
            .Push(Bob, HudText.Raw("b1"), Now, TimeSpan.FromSeconds(4))
            .Push(Alice, HudText.Raw("a2"), Now, TimeSpan.FromSeconds(4))
            .Push(Alice, HudText.Raw("a3"), Now, TimeSpan.FromSeconds(4));
        Assert.Equal(new[] { "all", "a2", "a3" }, widget.Render(Alice, Now).Select(l => l.Text.Literal));
        Assert.Equal(new[] { "all", "b1" }, widget.Render(Bob, Now).Select(l => l.Text.Literal));
        Assert.Empty(widget.Render(Alice, Now.AddSeconds(4)));
    }

    [Fact]
    public void Alerts_DropExpiredEntriesWhenPushing()
    {
        var widget = AlertsWidget.Empty
            .Push(Alice, HudText.Raw("old"), Now, TimeSpan.FromSeconds(1))
            .Push(Alice, HudText.Raw("new"), Now.AddSeconds(2), TimeSpan.FromSeconds(4));
        Assert.Equal("new", Assert.Single(widget.Entries).Text.Literal);
    }

    [Fact]
    public void Compose_OrdersByPriority_SkipsEmptyBlocks_AndCapsTheLines()
    {
        IReadOnlyList<HudLine> Lines(params string[] texts) => texts.Select(t => new HudLine(HudText.Raw(t), HudStyle.Text)).ToList();
        var composed = CenterComposer.Compose(new[] { (10, Lines("r1", "r2")), (30, Lines("a1")), (20, Lines()) }, 2);
        Assert.Equal(new[] { "a1", "r1" }, composed.Select(l => l.Text.Literal));
    }

    [Fact]
    public void Format_EscapesHtml_AndColorsEachLine()
    {
        var html = CenterHtml.Format(new[] { ("<b>Full</b> & co", HudStyle.Accent), ("CT 4", HudStyle.Muted) }, Theme);
        Assert.Equal("<font color='#4FC3F7'>&lt;b&gt;Full&lt;/b&gt; &amp; co</font><br><font color='#9E9E9E'>CT 4</font>", html);
        Assert.Null(CenterHtml.Format(Array.Empty<(string, HudStyle)>(), Theme));
    }
}

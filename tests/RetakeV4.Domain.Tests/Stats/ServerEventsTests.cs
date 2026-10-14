using RetakeV4.Domain.Events;
using RetakeV4.Domain.MapVote;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class ServerEventsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RepeatedEvents_BecomeOneRowPerKey()
    {
        var aggregator = new ServerEventAggregator();
        for (var i = 0; i < 5000; i++)
        {
            aggregator.Add(ServerEventTypes.Chat, null, "say");
        }
        aggregator.Add(ServerEventTypes.Chat, null, "say_team");
        aggregator.Add(ServerEventTypes.Command, "Admin", "css_retake");
        Assert.Equal(
            new[]
            {
                new ServerEvent(At, "chat", null, "say", 5000),
                new ServerEvent(At, "chat", null, "say_team", 1),
                new ServerEvent(At, "command", "Admin", "css_retake", 1),
            },
            aggregator.Flush(At));
        Assert.Equal(0, aggregator.Pending);
        Assert.Empty(aggregator.Flush(At));
    }

    [Fact]
    public void MeasuredEvents_KeepTheMaximum()
    {
        var aggregator = new ServerEventAggregator();
        aggregator.Add(new SlowHandlerReported("Hud", "tick", 60));
        aggregator.Add(new SlowHandlerReported("Hud", "tick", 120));
        aggregator.Add(new SlowHandlerReported("Hud", "tick", 80));
        aggregator.Add(ServerEventTypes.LongFrame, null, null, 70);
        Assert.Equal(
            new[] { new ServerEvent(At, "long_frame", null, null, 1, 70), new ServerEvent(At, "slow_handler", "Hud", "tick", 3, 120) },
            aggregator.Flush(At));
    }

    [Fact]
    public void ADisablingFailure_AlsoRecordsModuleDisabled()
    {
        var aggregator = new ServerEventAggregator();
        aggregator.Add(new ModuleFailureReported("Spawns", "bus:X", false));
        aggregator.Add(new ModuleFailureReported("Spawns", "bus:X", true));
        Assert.Equal(
            new[] { new ServerEvent(At, "module_disabled", "Spawns", "bus:X", 1), new ServerEvent(At, "module_error", "Spawns", "bus:X", 2) },
            aggregator.Flush(At));
    }

    [Fact]
    public void NewKeys_BeyondTheCap_AreDropped()
    {
        var aggregator = new ServerEventAggregator();
        for (var i = 0; i < ServerEventAggregator.MaxKeys + 10; i++)
        {
            aggregator.Add(ServerEventTypes.Command, "M", "c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        aggregator.Add(ServerEventTypes.Command, "M", "c0");
        Assert.Equal(ServerEventAggregator.MaxKeys, aggregator.Pending);
        Assert.Equal(10, aggregator.DroppedKeys);
        Assert.Equal(2, aggregator.Flush(At).Single(e => e.Value == "c0").Occurrences);
    }

    [Fact]
    public void Add_NeedsAType() => Assert.Throws<ArgumentException>(() => new ServerEventAggregator().Add(" ", null, null));

    [Theory]
    [InlineData(false, "plugin_start")]
    [InlineData(true, "hot_reload")]
    public void Started_TellsAColdStartFromAHotReload(bool hotReload, string type) =>
        Assert.Equal(new ServerEvent(At, type, null, "4.8.1"), ServerEventFactory.Started(At, hotReload, "4.8.1"));

    [Fact]
    public void Uptime_IsInWholeSeconds() =>
        Assert.Equal(new ServerEvent(At, "uptime", null, "600"), ServerEventFactory.Uptime(At, TimeSpan.FromSeconds(600.7)));

    [Fact]
    public void MapVoteRows_GiveEveryMapAndTheResult()
    {
        var closed = new MapVoteClosed("de_nuke", 2, new[] { new MapVoteCount("de_ancient", 1), new MapVoteCount("de_nuke", 2) });
        Assert.Equal(
            new[]
            {
                new ServerEvent(At, "map_vote", null, "de_ancient", 1),
                new ServerEvent(At, "map_vote", null, "de_nuke", 2),
                new ServerEvent(At, "map_vote_result", null, "de_nuke", 2),
            },
            ServerEventFactory.MapVoteRows(At, closed));
    }
}

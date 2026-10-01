using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class QueueChangesTests
{
    private static TeamState Queue(params QueuedPlayer[] queued) => TeamState.Empty with { Queue = queued };

    [Fact]
    public void NewEntries_AreReportedWithTheirPosition()
    {
        var before = Queue(new QueuedPlayer(new PlayerId(1), 0, 1));
        var after = Queue(new QueuedPlayer(new PlayerId(1), 0, 1), new QueuedPlayer(new PlayerId(2), 1, 2));
        Assert.Equal(new[] { (new PlayerId(2), 1) }, QueueChanges.NewlyQueued(before, after));
    }

    [Fact]
    public void OnlyNewEntries_AreReported()
    {
        var state = Queue(new QueuedPlayer(new PlayerId(1), 0, 1));
        Assert.Empty(QueueChanges.NewlyQueued(state, state));
        Assert.Empty(QueueChanges.NewlyQueued(state, TeamState.Empty));
    }
}

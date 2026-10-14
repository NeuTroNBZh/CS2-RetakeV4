using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Stats;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

public class StatsWriteQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    private static RoundSummary Round(int number, string map = "de_mirage")
    {
        var summary = StatsSamples.Round(Now);
        var context = summary.Record.Context with { RoundNumber = number, MapName = map };
        return summary with { Record = summary.Record with { Context = context } };
    }

    private static StatsWriteQueue Queue(FakeStatsRepository repository, ListLogger logger, int capacity = 100, int drainMs = 2000, int maxAttempts = StatsWriteQueue.DefaultMaxAttempts) =>
        new(repository, logger, capacity, TimeSpan.FromMilliseconds(drainMs), _ => TimeSpan.FromMilliseconds(5), maxAttempts);

    [Fact]
    public async Task Commands_RunInOrder_WithMapAndSessionIds()
    {
        var repository = new FakeStatsRepository();
        var queue = Queue(repository, new ListLogger());
        queue.Enqueue(new OpenMapCommand("de_mirage", Now));
        queue.Enqueue(new OpenSessionCommand(7, "p", Now));
        queue.Enqueue(new WriteRoundCommand(Round(1)));
        queue.Enqueue(new CloseSessionCommand(7, Now, 1, new Playtime(1, 0, 0, 0)));
        queue.Enqueue(new CloseMapCommand(Now));
        queue.Enqueue(new PurgeCommand(Now));
        Assert.True(await queue.Initialized);
        await queue.DisposeAsync();
        Assert.Equal(
            new[] { "OpenMap de_mirage -> 1", "OpenSession 7 map 1 -> 2", "Round 1 map 1", "CloseSession 7 session 2 map 1", "CloseMap 1", $"Purge {StatsWriteQueue.PurgeBatchSize}" },
            repository.Calls);
    }

    [Fact]
    public async Task RoundWithoutAnOpenMap_OpensItsMap()
    {
        var repository = new FakeStatsRepository();
        var queue = Queue(repository, new ListLogger());
        queue.Enqueue(new WriteRoundCommand(Round(1, "de_nuke")));
        queue.Enqueue(new WriteRoundCommand(Round(2, "de_nuke")));
        queue.Enqueue(new CloseSessionCommand(7, Now, null, new Playtime(1, 0, 0, 0)));
        await queue.DisposeAsync();
        Assert.Equal(new[] { "OpenMap de_nuke -> 1", "Round 1 map 1", "Round 2 map 1", "CloseSession 7 session none map 1" }, repository.Calls);
    }

    [Fact]
    public async Task FailedWrite_IsRetried_UntilItSucceeds()
    {
        var repository = new FakeStatsRepository { RoundFailuresLeft = 2 };
        var logger = new ListLogger();
        var queue = Queue(repository, logger);
        queue.Enqueue(new OpenMapCommand("de_mirage", Now));
        queue.Enqueue(new WriteRoundCommand(Round(1)));
        await queue.DisposeAsync();
        Assert.Contains("Round 1 map 1", repository.Calls);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
        Assert.Equal(0, queue.Abandoned);
    }

    [Fact]
    public async Task FullQueue_DropsTheOldestCommands()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new FakeStatsRepository { InitBlock = gate.Task };
        var logger = new ListLogger();
        var queue = Queue(repository, logger, capacity: 2);
        for (var round = 1; round <= 5; round++)
        {
            queue.Enqueue(new WriteRoundCommand(Round(round)));
        }
        Assert.Equal(3, queue.Dropped);
        gate.SetResult();
        await queue.DisposeAsync();
        Assert.Equal(new[] { "OpenMap de_mirage -> 1", "Round 4 map 1", "Round 5 map 1" }, repository.Calls);
        Assert.Equal(3, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task InitializationFailure_IsRetried_ThenQueuedCommandsAreWrittenInOrder()
    {
        var repository = new FakeStatsRepository { InitFailuresLeft = 2 };
        var logger = new ListLogger();
        var queue = Queue(repository, logger);
        queue.Enqueue(new OpenMapCommand("de_mirage", Now));
        queue.Enqueue(new WriteRoundCommand(Round(1)));
        queue.Enqueue(new WriteRoundCommand(Round(2)));
        Assert.True(await queue.Initialized.WaitAsync(TimeSpan.FromSeconds(5)));
        await queue.DisposeAsync();
        Assert.Equal(3, repository.InitCalls);
        Assert.Equal(new[] { "OpenMap de_mirage -> 1", "Round 1 map 1", "Round 2 map 1" }, repository.Calls);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(0, queue.Abandoned);
    }

    [Fact]
    public async Task Dispose_WhileInitializationKeepsFailing_IsBounded_AndAbandonsTheCommands()
    {
        var repository = new FakeStatsRepository { InitFailuresLeft = int.MaxValue };
        var logger = new ListLogger();
        var queue = Queue(repository, logger, drainMs: 200);
        queue.Enqueue(new OpenMapCommand("de_mirage", Now));
        queue.Enqueue(new WriteRoundCommand(Round(1)));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await queue.DisposeAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.False(await queue.Initialized.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(repository.InitCalls > 1);
        Assert.Empty(repository.Calls);
        Assert.Equal(2, queue.Abandoned);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Dispose_GivesUp_OnAPermanentFailure()
    {
        var repository = new FakeStatsRepository { RoundFailuresLeft = int.MaxValue };
        var logger = new ListLogger();
        var queue = Queue(repository, logger, drainMs: 200, maxAttempts: 1000);
        queue.Enqueue(new OpenMapCommand("de_mirage", Now));
        queue.Enqueue(new WriteRoundCommand(Round(1)));
        queue.Enqueue(new WriteRoundCommand(Round(2)));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await queue.DisposeAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.Equal(2, queue.Abandoned);
        Assert.Contains(logger.Entries, e => e.Message.Contains("abandoned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PoisonCommand_IsDropped_AfterMaxAttempts_AndTheQueueMovesOn()
    {
        var repository = new FakeStatsRepository { RoundFailuresLeft = int.MaxValue };
        var logger = new ListLogger();
        var queue = Queue(repository, logger, maxAttempts: 3);
        queue.Enqueue(new OpenMapCommand("de_mirage", Now));
        queue.Enqueue(new WriteRoundCommand(Round(1)));
        queue.Enqueue(new PurgeCommand(Now));
        await queue.DisposeAsync();
        Assert.Equal(1, queue.Failed);
        Assert.Equal(0, queue.Abandoned);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal(new[] { "OpenMap de_mirage -> 1", $"Purge {StatsWriteQueue.PurgeBatchSize}" }, repository.Calls);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var logger = new ListLogger();
        var queue = Queue(new FakeStatsRepository(), logger);
        await queue.DisposeAsync();
        await queue.DisposeAsync();
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Dispose_IsBounded_EvenWhenCancellationBlocks()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new FakeStatsRepository { InitBlock = gate.Task };
        var queue = Queue(repository, new ListLogger(), drainMs: 100);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await queue.DisposeAsync();
        gate.TrySetResult();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5000)]
    public void NegativeOrInfiniteDrainTimeout_IsRejected(int ms) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatsWriteQueue(new FakeStatsRepository(), new ListLogger(), 1, TimeSpan.FromMilliseconds(ms)));

    [Fact]
    public async Task Enqueue_AfterDispose_CountsAsAbandoned()
    {
        var queue = Queue(new FakeStatsRepository(), new ListLogger());
        await queue.DisposeAsync();
        queue.Enqueue(new PurgeCommand(Now));
        Assert.Equal(1, queue.Abandoned);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 60)]
    [InlineData(30, 60)]
    public void DefaultBackoff_DoublesUpToAMinute(int attempt, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), StatsWriteQueue.DefaultBackoff(attempt));
}

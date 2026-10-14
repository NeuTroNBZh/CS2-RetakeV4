using RetakeV4.Domain.Stats;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

internal sealed class FakeStatsRepository : IStatsRepository
{
    private readonly object _lock = new();
    private readonly List<string> _calls = new();
    private long _nextId = 1;

    public Task InitBlock { get; set; } = Task.CompletedTask;

    // Number of InitializeAsync calls that throw before one succeeds (int.MaxValue: the database never comes up).
    public int InitFailuresLeft { get; set; }

    public int InitCalls { get; private set; }

    public int RoundFailuresLeft { get; set; }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToList();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await InitBlock.WaitAsync(ct);
        lock (_lock)
        {
            InitCalls++;
            if (InitFailuresLeft > 0)
            {
                InitFailuresLeft--;
                throw new InvalidOperationException("no database");
            }
        }
    }

    public Task<long> OpenMapAsync(string mapName, DateTimeOffset startedAt, CancellationToken ct)
    {
        var id = Next();
        Record($"OpenMap {mapName} -> {id}");
        return Task.FromResult(id);
    }

    public Task CloseMapAsync(long mapId, DateTimeOffset endedAt, CancellationToken ct)
    {
        Record($"CloseMap {mapId}");
        return Task.CompletedTask;
    }

    public Task<long> OpenSessionAsync(long mapId, ulong steamId, string name, DateTimeOffset connectedAt, CancellationToken ct)
    {
        var id = Next();
        Record($"OpenSession {steamId} map {mapId} -> {id}");
        return Task.FromResult(id);
    }

    public Task CloseSessionAsync(long? sessionId, long mapId, ulong steamId, DateTimeOffset disconnectedAt, int? reason, Playtime playtime, CancellationToken ct)
    {
        Record($"CloseSession {steamId} session {sessionId?.ToString() ?? "none"} map {mapId}");
        return Task.CompletedTask;
    }

    public Task<long> WriteRoundAsync(long mapId, RoundSummary round, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            if (RoundFailuresLeft > 0)
            {
                RoundFailuresLeft--;
                throw new InvalidOperationException("database down");
            }
        }
        Record($"Round {round.Record.Context.RoundNumber} map {mapId}");
        return Task.FromResult(Next());
    }

    public Task<IReadOnlyDictionary<ulong, int>> LoadEloAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<ulong, int>>(new Dictionary<ulong, int>());

    public Task<int> PurgeRawAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct)
    {
        Record($"Purge {batchSize}");
        return Task.FromResult(0);
    }

    private long Next() => Interlocked.Increment(ref _nextId) - 1;

    private void Record(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }
    }
}

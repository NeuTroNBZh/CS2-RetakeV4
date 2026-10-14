using RetakeV4.Domain.Stats;

namespace RetakeV4.Persistence.Stats;

public sealed class NoOpStatsRepository : IStatsRepository
{
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<long> OpenMapAsync(string mapName, DateTimeOffset startedAt, CancellationToken ct) => Task.FromResult(0L);

    public Task CloseMapAsync(long mapId, DateTimeOffset endedAt, CancellationToken ct) => Task.CompletedTask;

    public Task<long> OpenSessionAsync(long mapId, ulong steamId, string name, DateTimeOffset connectedAt, CancellationToken ct) => Task.FromResult(0L);

    public Task CloseSessionAsync(long? sessionId, long mapId, ulong steamId, DateTimeOffset disconnectedAt, int? reason, Playtime playtime,
        SessionActivity activity, CancellationToken ct) => Task.CompletedTask;

    public Task SetCountryAsync(long? sessionId, ulong steamId, string country, CancellationToken ct) => Task.CompletedTask;

    public Task WritePresenceAsync(PresenceSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;

    public Task WriteServerEventsAsync(IReadOnlyList<ServerEvent> serverEvents, CancellationToken ct) => Task.CompletedTask;

    public Task<long> WriteRoundAsync(long mapId, RoundSummary round, CancellationToken ct) => Task.FromResult(0L);

    public Task<IReadOnlyDictionary<ulong, int>> LoadEloAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<ulong, int>>(new Dictionary<ulong, int>());

    public Task<int> PurgeRawAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct) => Task.FromResult(0);
}

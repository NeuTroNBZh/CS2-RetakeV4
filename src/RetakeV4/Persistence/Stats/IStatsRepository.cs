using RetakeV4.Domain.Stats;

namespace RetakeV4.Persistence.Stats;

public sealed record StatsWriteOptions(string ServerKey, int EloStart, int EloFloor);

// Called only from the StatsWriteQueue background worker, never from the game thread.
public interface IStatsRepository
{
    Task InitializeAsync(CancellationToken ct);

    Task<long> OpenMapAsync(string mapName, DateTimeOffset startedAt, CancellationToken ct);

    Task CloseMapAsync(long mapId, DateTimeOffset endedAt, CancellationToken ct);

    Task<long> OpenSessionAsync(long mapId, ulong steamId, string name, DateTimeOffset connectedAt, CancellationToken ct);

    // sessionId is null when the opening was lost (queue overflow): the session row is then created closed.
    Task CloseSessionAsync(long? sessionId, long mapId, ulong steamId, DateTimeOffset disconnectedAt, int? reason, Playtime playtime, CancellationToken ct);

    Task<long> WriteRoundAsync(long mapId, RoundSummary round, CancellationToken ct);

    Task<IReadOnlyDictionary<ulong, int>> LoadEloAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct);

    Task<int> PurgeRawAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct);
}

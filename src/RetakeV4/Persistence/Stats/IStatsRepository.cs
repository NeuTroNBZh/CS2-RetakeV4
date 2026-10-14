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
    Task CloseSessionAsync(long? sessionId, long mapId, ulong steamId, DateTimeOffset disconnectedAt, int? reason, Playtime playtime,
        SessionActivity activity, CancellationToken ct);

    // Sets the country of the player and of their open session (sessionId null: the player only).
    Task SetCountryAsync(long? sessionId, ulong steamId, string country, CancellationToken ct);

    Task WritePresenceAsync(PresenceSnapshot snapshot, CancellationToken ct);

    // All rows in one transaction; an empty list writes nothing.
    Task WriteServerEventsAsync(IReadOnlyList<ServerEvent> serverEvents, CancellationToken ct);

    Task<long> WriteRoundAsync(long mapId, RoundSummary round, CancellationToken ct);

    // Keeps the replay of a round already written (roundId) for the claims that win: ReplaySelection.Decide is applied to
    // each claim against the player's current ownerships, in one transaction that first locks the claimants' stats_players
    // rows (claimants without a row are ignored), so that servers sharing the database agree. Tracks are written only when
    // the round has an owner and no stored replay yet; replays left without an owner are deleted with their tracks.
    // Returns the number of owners the round has afterwards (0 = not stored).
    Task<int> WriteReplayAsync(long roundId, ReplayRound replay, IReadOnlyList<ReplayClaim> claims, ReplayCaps caps, DateTimeOffset at, CancellationToken ct);

    Task<IReadOnlyDictionary<ulong, int>> LoadEloAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct);

    Task<int> PurgeRawAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct);
}

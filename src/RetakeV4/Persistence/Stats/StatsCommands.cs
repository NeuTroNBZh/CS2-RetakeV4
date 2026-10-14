using RetakeV4.Domain.Stats;

namespace RetakeV4.Persistence.Stats;

// Work posted by the game thread; ids (map, sessions) are resolved by the queue worker, never on the game thread.
public abstract record StatsCommand;

public sealed record OpenMapCommand(string MapName, DateTimeOffset At) : StatsCommand;

public sealed record CloseMapCommand(DateTimeOffset At) : StatsCommand;

public sealed record OpenSessionCommand(ulong SteamId, string Name, DateTimeOffset At) : StatsCommand;

public sealed record CloseSessionCommand(ulong SteamId, DateTimeOffset At, int? Reason, Playtime Playtime) : StatsCommand
{
    public SessionActivity Activity { get; init; } = SessionActivity.None;
}

public sealed record WriteRoundCommand(RoundSummary Round) : StatsCommand
{
    // Ties later WriteReplayCommands to this round (StatsModule numbers the rounds of a module load from 1); 0 = none.
    public long RoundKey { get; init; }
}

// The replay of a round queued earlier by a WriteRoundCommand with the same RoundKey, and the claims on it: best and
// highlight claims right after the round, one saved claim per !save. It holds the frame text (about 250 KB), so
// StatsWriteQueue keeps at most MaxPendingReplays of them and skips them, like telemetry, once half of the queue is pending.
public sealed record WriteReplayCommand(long RoundKey, ReplayRound Replay, IReadOnlyList<ReplayClaim> Claims, ReplayCaps Caps, DateTimeOffset At) : StatsCommand;

public sealed record PurgeCommand(DateTimeOffset OlderThan) : StatsCommand;

// The country is resolved off the game thread before this command exists: the IP address never reaches the queue.
// Ordering requirement: enqueue OpenSessionCommand first. The repository only updates an existing stats_players row,
// so a country sent before the session is opened is silently lost.
public sealed record PlayerCountryCommand(ulong SteamId, string Country) : StatsCommand;

// Telemetry (presence, server health, uptime) yields to the game data: StatsWriteQueue skips it, instead of queuing it,
// once the queue is half full, so that a database outage never pushes rounds or sessions out of the bounded queue.
public abstract record TelemetryCommand : StatsCommand;

public sealed record PresenceCommand(PresenceSnapshot Snapshot) : TelemetryCommand;

// Every row of one flush (aggregated events, a map vote) in one command and one transaction.
public sealed record ServerEventsCommand(IReadOnlyList<ServerEvent> Events) : TelemetryCommand;

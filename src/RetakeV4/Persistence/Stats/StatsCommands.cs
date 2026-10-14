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

public sealed record WriteRoundCommand(RoundSummary Round) : StatsCommand;

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

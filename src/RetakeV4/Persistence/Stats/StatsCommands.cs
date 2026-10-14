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

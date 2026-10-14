using RetakeV4.Domain.MapVote;

namespace RetakeV4.Domain.Events;

// ModuleGuard reports, republished by ServerHealthRelay for the Stats module.
public sealed record ModuleFailureReported(string Module, string Stage, bool ModuleDisabled);

public sealed record SlowHandlerReported(string Module, string Stage, int ElapsedMs);

// A plugin command was run (ModuleHooks.Command): the command name only, never its arguments.
public sealed record PluginCommandUsed(string Module, string Command);

// A map vote closed with this winner; Tally lists every map of the vote with its votes.
public sealed record MapVoteClosed(string Winner, int Votes, IReadOnlyList<MapVoteCount> Tally);

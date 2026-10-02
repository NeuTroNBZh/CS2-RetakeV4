using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Events;

// Remote control (console / RCON). Reply writes one line back to the caller, synchronously.
public sealed record MapVoteRequested(Action<string> Reply);

public sealed record MapCleanupReplayRequested(Action<string> Reply);

public sealed record MapVoteStateChanged(bool Open, string? NextMap);

public sealed record MapCleanupEditorStateChanged(bool Active);

// Null: no site is forced.
public sealed record SiteForceChanged(SiteForce? Force);

public sealed record ScrambleStateChanged(bool Pending);

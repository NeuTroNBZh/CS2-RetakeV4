using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Events;

// Requester null: the server console. The publisher has already checked the permission.
public sealed record ForceSiteRequested(PlayerId? Requester, ForceSiteRequest Request);

public sealed record ScrambleRequested(PlayerId? Requester);

// CounterStrikeSharp's OnAllPluginsLoaded: other plugins' capabilities (CS2-SimpleAdmin) can be resolved from here on.
public sealed record AllPluginsLoaded(bool HotReload);

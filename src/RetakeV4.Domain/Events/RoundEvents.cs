using RetakeV4.Domain.Common;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Events;

public sealed record RoundPhaseChanged(RoundPhase From, RoundPhase To, int RoundNumber);

public sealed record RoundPrepared(PreparationContext Context);

public sealed record MapStarted(string MapName);

public sealed record WarmupForcedEnd(string MapName);

public sealed record ModulesReady(bool HotReload);

public sealed record BombPlanted(BombSite? Site, PlayerId? Planter);

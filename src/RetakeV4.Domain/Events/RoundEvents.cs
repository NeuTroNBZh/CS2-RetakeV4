using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Events;

public sealed record RoundPhaseChanged(RoundPhase From, RoundPhase To, int RoundNumber);

public sealed record RoundPrepared(PreparationContext Context);

public sealed record MapStarted(string MapName);

public sealed record WarmupForcedEnd(string MapName);

public sealed record ModulesReady(bool HotReload);

public sealed record BombPlanted(BombSite? Site, PlayerId? Planter);

public sealed record RoundTypesLoaded(IReadOnlyList<RoundTypeDefinition> Definitions);

public sealed record TeamStateChanged(TeamState State);

public sealed record LoadoutsAssigned(int RoundNumber, IReadOnlyDictionary<PlayerId, Loadout> Loadouts);

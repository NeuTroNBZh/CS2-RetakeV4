using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Stats;

public sealed record RoundParticipant(ulong SteamId, string Name, TeamSide Side, int EloBefore, string? PrimaryWeapon);

public sealed record RoundContext(
    string MapName, int RoundNumber, BombSite? Site, string? RoundType, DateTimeOffset StartedAt, IReadOnlyList<RoundParticipant> Participants);

// Winner is null for a draw; EndReason is the raw round_end reason.
public sealed record RoundOutcome(TeamSide? Winner, int EndReason, int DurationMs);

public sealed record RoundRecord(
    RoundContext Context,
    RoundOutcome Outcome,
    bool Ranked,
    IReadOnlyList<ShotFired> Shots,
    IReadOnlyList<DamageDealt> Damages,
    IReadOnlyList<PlayerKilled> Kills,
    IReadOnlyList<GrenadeThrown> GrenadesThrown,
    IReadOnlyList<GrenadeDetonated> GrenadesDetonated,
    IReadOnlyList<PlayerBlinded> Blinds,
    BombPlantedFact? Plant,
    BombDefusedFact? Defuse,
    BombExplodedFact? Explosion,
    IReadOnlyList<MvpAwarded> Mvps,
    IReadOnlyList<PlayerLeftRound> Leavers)
{
    public IReadOnlyList<PlayerEventFact> Events { get; init; } = Array.Empty<PlayerEventFact>();

    // One state per player (the last one read).
    public IReadOnlyList<PlayerEndState> EndStates { get; init; } = Array.Empty<PlayerEndState>();

    // Milliseconds from the round start to round_freeze_end; null when it was not seen.
    public int? FreezeEndMs { get; init; }
}

using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Teams;

public enum RoundWinner
{
    None,
    T,
    CT,
}

public enum MoveReason
{
    SwitchedAfterCtWin,
    EnteredFromQueue,
    Scrambled,
    Balanced,
}

public sealed record TeamMove(PlayerId Player, TeamSide To, MoveReason Reason);

public sealed record TeamPlan(TeamState State, IReadOnlyList<TeamMove> Moves, bool Scrambled);

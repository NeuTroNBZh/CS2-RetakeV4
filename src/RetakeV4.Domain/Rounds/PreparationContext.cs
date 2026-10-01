using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Rounds;

public sealed record PreparationContext(int RoundNumber)
{
    public int RoundsPlayed { get; init; }

    public string? RoundType { get; init; }

    public BombSite? Site { get; init; }

    public PlayerId? Planter { get; init; }
}

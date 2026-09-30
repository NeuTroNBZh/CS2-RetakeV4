namespace RetakeV4.Domain.Rounds;

public sealed record RoundState(RoundPhase Phase, int RoundNumber)
{
    public static RoundState Initial { get; } = new(RoundPhase.Warmup, 0);
}

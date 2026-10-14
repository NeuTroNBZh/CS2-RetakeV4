namespace RetakeV4.Domain.Stats;

public sealed record RatingWeights(double Base, double Kill, double Survive, double Damage, double Kast, double Impact);

public sealed record EloRules(int Start, int Floor, int K, int PlayerAdvantage);

// Built from StatsConfig; the Domain holds no default values.
public sealed record StatsRules(double TradeWindowSeconds, int MinPlayersPerSide, int MaxShotsPerRound, RatingWeights Rating, EloRules Elo)
{
    // Null: no movement or behaviour stats (sampling off).
    public MovementRules? Movement { get; init; }
}

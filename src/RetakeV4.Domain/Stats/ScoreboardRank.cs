namespace RetakeV4.Domain.Stats;

// What the scoreboard (TAB) shows in the rank column: the player's ELO as a CS Rating (Premier) value. The game picks the
// colour from the value itself. Nothing is shown until the stored ELO is known, so a player never flashes the start value.
public readonly record struct ScoreboardRank(sbyte RankType, int Value)
{
    public const sbyte PremierRankType = 11;

    public static ScoreboardRank None { get; } = new(0, 0);

    public static ScoreboardRank For(bool enabled, bool loaded, int elo) =>
        enabled && loaded ? new ScoreboardRank(PremierRankType, Math.Max(0, elo)) : None;
}

namespace RetakeV4.Domain.Stats;

// Per-round performance; a scope's rating is the mean over its rounds (rating_sum / rounds).
public static class RetakeRating
{
    public static double Impact(PlayerRoundStats player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return (player.EntryKill ? 0.5 : 0)
            + (player.ClutchWon ? 0.5 * player.ClutchVs : 0)
            + (player.Defused ? 0.5 : 0)
            + (player.Planted ? 0.3 : 0)
            + (player.Kills >= 3 ? 0.3 : 0);
    }

    public static double For(PlayerRoundStats player, RatingWeights weights)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(weights);
        var rating = weights.Base
            + weights.Kill * player.Kills
            + weights.Survive * (player.Survived ? 1 : 0)
            + weights.Damage * player.Damage
            + weights.Kast * (player.Kast ? 1 : 0)
            + weights.Impact * Impact(player);
        return Math.Round(rating, 3, MidpointRounding.AwayFromZero);
    }
}

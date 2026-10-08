using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Plant;

public enum AutoPlantDecision
{
    Plant,
    Warmup,
    NotEnoughPlayers,
    NoPlanter,
    NoSite,
}

public static class PlantRules
{
    public const int MinimumPlayers = 2;

    public static AutoPlantDecision Evaluate(bool isWarmup, int playersOnTeams, PlayerId? planter, BombSite? site)
    {
        if (isWarmup)
        {
            return AutoPlantDecision.Warmup;
        }
        if (playersOnTeams < MinimumPlayers)
        {
            return AutoPlantDecision.NotEnoughPlayers;
        }
        if (planter is null)
        {
            return AutoPlantDecision.NoPlanter;
        }
        return site is null ? AutoPlantDecision.NoSite : AutoPlantDecision.Plant;
    }

    public static bool CanAutoPlant(bool isWarmup, int playersOnTeams, PlayerId? planter, BombSite? site) =>
        Evaluate(isWarmup, playersOnTeams, planter, site) == AutoPlantDecision.Plant;
}

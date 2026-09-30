using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Plant;

public static class PlantRules
{
    public const int MinimumPlayers = 2;

    public static bool CanAutoPlant(bool isWarmup, int playersOnTeams, PlayerId? planter, BombSite? site) =>
        !isWarmup && playersOnTeams >= MinimumPlayers && planter is not null && site is not null;
}

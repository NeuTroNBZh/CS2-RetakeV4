using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.MapCleanup;

public static class DoorRoll
{
    public static bool ShouldOpen(int chancePercent, IRandom random) =>
        chancePercent >= 100 || (chancePercent > 0 && random.NextDouble() * 100 < chancePercent);
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace RetakeV4.Adapters;

internal static class GameRulesAccessor
{
    public static CCSGameRules? Get() =>
        Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault(proxy => proxy.IsValid)?.GameRules;

    public static bool IsWarmup() => Get()?.WarmupPeriod ?? true;

    public static int TotalRoundsPlayed() => Get()?.TotalRoundsPlayed ?? 0;
}

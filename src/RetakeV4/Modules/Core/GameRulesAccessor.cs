using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace RetakeV4.Modules.Core;

internal static class GameRulesAccessor
{
    public static CCSGameRules? Get() =>
        Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault(proxy => proxy.IsValid)?.GameRules;
}

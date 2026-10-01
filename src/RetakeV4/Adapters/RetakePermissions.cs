using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using RetakeV4.Domain.Admin;

namespace RetakeV4.Adapters;

internal static class RetakePermissions
{
    public static bool IsAdmin(CCSPlayerController player) => HasAny(player, AdminFlags.Admin);

    public static bool IsRoot(CCSPlayerController player) => HasAny(player, AdminFlags.Root);

    private static bool HasAny(CCSPlayerController player, IReadOnlyList<string> flags) =>
        player.IsValid && flags.Any(flag => AdminManager.PlayerHasPermissions(player, flag));
}

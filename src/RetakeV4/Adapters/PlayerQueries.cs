using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RetakeV4.Domain.Common;

namespace RetakeV4.Adapters;

internal static class PlayerQueries
{
    public static IReadOnlyList<CCSPlayerController> Humans() =>
        Utilities.GetPlayers()
            .Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false } && p.Connected == PlayerConnectedState.Connected)
            .ToList();

    public static TeamSide? SideOf(CCSPlayerController player) => (CsTeam)player.TeamNum switch
    {
        CsTeam.Terrorist => TeamSide.T,
        CsTeam.CounterTerrorist => TeamSide.CT,
        _ => null,
    };

    public static CsTeam ToCsTeam(TeamSide side) => side == TeamSide.T ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
}

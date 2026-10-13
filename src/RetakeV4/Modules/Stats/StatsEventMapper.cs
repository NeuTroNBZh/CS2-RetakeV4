using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Modules.Stats;

// Engine reads shared by the Stats handlers. Bots and HLTV are never actors (null SteamId).
internal static class StatsEventMapper
{
    public static ulong? HumanId(CCSPlayerController? player) =>
        player is { IsValid: true, IsBot: false, IsHLTV: false } && player.SteamID != 0 ? player.SteamID : null;

    public static bool IsBot(CCSPlayerController? player) => player is { IsValid: true, IsBot: true };

    public static Vec3? PositionOf(CCSPlayerController? player) =>
        player is { IsValid: true } && player.PlayerPawn.Value is { IsValid: true, AbsOrigin: { } origin } ? new Vec3(origin.X, origin.Y, origin.Z) : null;

    public static ViewAngles? AnglesOf(CCSPlayerController? player) =>
        player is { IsValid: true } && player.PlayerPawn.Value is { IsValid: true } pawn ? new ViewAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y) : null;

    public static bool HasKit(CCSPlayerController? player) =>
        player is { IsValid: true } && player.PlayerPawn.Value is { IsValid: true, ItemServices: { } items } && new CCSPlayer_ItemServices(items.Handle).HasDefuser;

    public static TeamSide? WinnerOf(int winner) => (CsTeam)winner switch
    {
        CsTeam.Terrorist => TeamSide.T,
        CsTeam.CounterTerrorist => TeamSide.CT,
        _ => null,
    };
}

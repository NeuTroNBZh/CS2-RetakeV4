using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Modules.Stats;

// Engine reads shared by the Stats handlers. Bots and HLTV are never actors (null SteamId).
internal static class StatsEventMapper
{
    // FL_ONGROUND: bit 0 of the pawn flags.
    private const uint OnGround = 1;

    public static ulong? HumanId(CCSPlayerController? player) =>
        player is { IsValid: true, IsBot: false, IsHLTV: false } && player.SteamID != 0 ? player.SteamID : null;

    public static bool IsBot(CCSPlayerController? player) => player is { IsValid: true, IsBot: true };

    public static bool IsOnGround(CCSPlayerController? player) =>
        player is { IsValid: true } && player.PlayerPawn.Value is { IsValid: true } pawn && (pawn.Flags & OnGround) != 0;

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

    // Pawn state at weapon_fire: horizontal speed, crouched, scoped, in the air, walking.
    public static ShotMotion? MotionOf(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            return null;
        }
        var velocity = pawn.AbsVelocity;
        var crouched = pawn.MovementServices is { } movement && new CCSPlayer_MovementServices(movement.Handle).Ducked;
        return new ShotMotion(ShotMotion.Horizontal(velocity.X, velocity.Y), crouched, pawn.IsScoped, (pawn.Flags & OnGround) == 0, pawn.IsWalking);
    }

    // Living pawn state for a movement sample; null for a dead or unreadable pawn. Reads only, no collection or string.
    public static MovementSample? SampleOf(ulong steamId, CCSPlayerController player, int timeMs)
    {
        if (!player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true, AbsOrigin: { } origin } pawn)
        {
            return null;
        }
        var velocity = pawn.AbsVelocity;
        var angles = pawn.EyeAngles;
        var crouched = pawn.MovementServices is { } movement && new CCSPlayer_MovementServices(movement.Handle).Ducked;
        var flags = MovementSample.FlagsOf(crouched, pawn.IsWalking, (pawn.Flags & OnGround) == 0);
        return new MovementSample(steamId, timeMs, new Vec3(origin.X, origin.Y, origin.Z), ShotMotion.Horizontal(velocity.X, velocity.Y),
            angles.Y, angles.X, flags, pawn.Health);
    }

    // Designer name, rounds left and magazine size of the active weapon (weapon_reload carries no weapon).
    public static (string Weapon, int Clip, int MaxClip)? ActiveWeaponOf(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || player.PlayerPawn.Value is not { IsValid: true, WeaponServices: { } weapons }
            || weapons.ActiveWeapon.Value is not { IsValid: true } weapon)
        {
            return null;
        }
        return (weapon.DesignerName, weapon.Clip1, weapon.VData?.MaxClip1 ?? 0);
    }

    public static bool CarriesBomb(CCSPlayerController? player) =>
        player is { IsValid: true } && player.PlayerPawn.Value is { IsValid: true, WeaponServices: { } weapons }
        && weapons.MyWeapons.Any(w => w.Value is { IsValid: true, DesignerName: "weapon_c4" });

    public static PlayerEndState EndStateOf(ulong steamId, CCSPlayerController player) =>
        player.PawnIsAlive && player.PlayerPawn.Value is { IsValid: true } pawn
            ? new PlayerEndState(steamId, pawn.Health, pawn.ArmorValue)
            : new PlayerEndState(steamId, 0, 0);

    // Time left on the planted bomb, read at bomb_defused (the entity stays until the round ends).
    public static int? BombRemainingMs()
    {
        var bomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(b => b.IsValid);
        return bomb is null ? null : BombClock.RemainingMs(bomb.C4Blow, Server.CurrentTime);
    }

    public static int BotCount() => Utilities.GetPlayers().Count(p => p is { IsValid: true, IsBot: true, IsHLTV: false });
}

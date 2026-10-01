using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Adapters;

internal sealed record PlayerViewPoint(Vec3 Eye, ViewAngles Angles, CBasePlayerPawn Pawn);

internal static class PlayerView
{
    private const int AttackDelayTicks = 2;

    // Dead players and spectators look through their observer pawn.
    public static PlayerViewPoint? Of(CCSPlayerController player)
    {
        if (player.PawnIsAlive && player.PlayerPawn.Value is { IsValid: true, AbsOrigin: { } origin } pawn)
        {
            var eye = new Vec3(origin.X, origin.Y, origin.Z + pawn.ViewOffset.Z);
            return new PlayerViewPoint(eye, new ViewAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y), pawn);
        }
        if (player.ObserverPawn.Value is { IsValid: true, AbsOrigin: { } position } observer)
        {
            var eye = new Vec3(position.X, position.Y, position.Z + observer.ViewOffset.Z);
            return new PlayerViewPoint(eye, new ViewAngles(observer.V_angle.X, observer.V_angle.Y), observer);
        }
        return null;
    }

    // Clicking an aimed menu line must not fire the weapon.
    public static void DelayAttack(CCSPlayerController player)
    {
        var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (weapon is not { IsValid: true })
        {
            return;
        }
        weapon.NextPrimaryAttackTick = Server.TickCount + AttackDelayTicks;
        weapon.NextSecondaryAttackTick = Server.TickCount + AttackDelayTicks;
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
    }
}

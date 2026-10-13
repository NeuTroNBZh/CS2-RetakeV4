using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Stats;

public enum GrenadeType
{
    He,
    Flash,
    Smoke,
    Molotov,
    Decoy,
}

// Times are milliseconds since the round started; ticks are server ticks. Bots are never actors: their SteamId is 0 and
// they only appear as victims flagged VictimIsBot. Weapons are normalized (StatsWeapons.Normalize).
public sealed record ShotFired(ulong Shooter, string Weapon, int TimeMs, int Tick, Vec3? Position, ViewAngles? Angles);

public sealed record DamageDealt(
    ulong? Attacker, ulong Victim, bool VictimIsBot, string Weapon, int Hitgroup, int DmgHealth, int DmgArmor, int HealthAfter, int TimeMs, int Tick);

public sealed record PlayerKilled(
    ulong? Attacker, ulong Victim, bool VictimIsBot, ulong? Assister, bool FlashAssist, string Weapon, bool Headshot, int Penetrated,
    bool NoScope, bool ThroughSmoke, bool AttackerBlind, bool AttackerInAir, float Distance, Vec3? AttackerPosition, Vec3? VictimPosition, int TimeMs);

public sealed record GrenadeThrown(ulong Thrower, GrenadeType Type, int TimeMs, Vec3? Position);

// EntityId is null for molotovs (molotov_detonate carries none).
public sealed record GrenadeDetonated(ulong Thrower, GrenadeType Type, int? EntityId, int TimeMs, Vec3 Position);

public sealed record PlayerBlinded(ulong? Attacker, ulong Victim, int EntityId, float Seconds, int TimeMs);

public sealed record BombPlantedFact(ulong Planter, int TimeMs);

public sealed record BombDefusedFact(ulong Defuser, bool WithKit, int TimeMs);

public sealed record BombExplodedFact(int TimeMs);

public sealed record MvpAwarded(ulong Player, int Reason);

public sealed record PlayerLeftRound(ulong Player, int TimeMs);

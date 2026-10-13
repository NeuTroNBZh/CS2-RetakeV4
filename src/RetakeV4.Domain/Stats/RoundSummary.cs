using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Stats;

public sealed record WeaponRoundStats(string Weapon, int Shots, int Hits, int HeadshotHits, int Kills, int HeadshotKills, int Damage);

public sealed record PlayerRoundStats(ulong SteamId, string Name, TeamSide Side)
{
    public string? PrimaryWeapon { get; init; }

    public bool Survived { get; init; }

    public bool Won { get; init; }

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public int Assists { get; init; }

    public int FlashAssists { get; init; }

    public int TeamKills { get; init; }

    public int Damage { get; init; }

    public int UtilityDamage { get; init; }

    public int HeadshotKills { get; init; }

    public int Shots { get; init; }

    public int Hits { get; init; }

    public int HeadshotHits { get; init; }

    public bool EntryKill { get; init; }

    public bool EntryDeath { get; init; }

    public int TradeKills { get; init; }

    public bool TradedDeath { get; init; }

    public bool Kast { get; init; }

    // 0 = never last alive against at least one opponent.
    public int ClutchVs { get; init; }

    public bool ClutchWon { get; init; }

    public int EnemiesFlashed { get; init; }

    public int TeammatesFlashed { get; init; }

    public double BlindSecondsInflicted { get; init; }

    public bool Mvp { get; init; }

    public bool Planted { get; init; }

    public bool Defused { get; init; }

    public bool DefusedWithKit { get; init; }

    public double Rating { get; init; }

    public int EloBefore { get; init; }

    public int EloDelta { get; init; }

    public int EloAfter { get; init; }

    public IReadOnlyList<WeaponRoundStats> Weapons { get; init; } = Array.Empty<WeaponRoundStats>();
}

public sealed record GrenadeSummary(
    ulong Thrower, GrenadeType Type, int? ThrownMs, int? DetonatedMs, Vec3? ThrowPosition, Vec3? DetonationPosition,
    int Damage, int EnemiesBlinded, int TeammatesBlinded, double BlindSeconds);

public sealed record RoundSummary(RoundRecord Record, IReadOnlyList<PlayerRoundStats> Players, IReadOnlyList<GrenadeSummary> Grenades);

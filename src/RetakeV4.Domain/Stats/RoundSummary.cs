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

    // Bullet shots split by the shooter's motion (StatsWeapons.IsStill); shots without a readable pawn are in neither.
    public int ShotsStill { get; init; }

    public int HitsStill { get; init; }

    public int ShotsMoving { get; init; }

    public int HitsMoving { get; init; }

    public int ShotsScoped { get; init; }

    public int HitsScoped { get; init; }

    public int Reloads { get; init; }

    // Reloads started with at least half of the magazine left (also counted in Reloads).
    public int ReloadsFull { get; init; }

    public int Pickups { get; init; }

    public int Drops { get; init; }

    public int Jumps { get; init; }

    public int PlantAttempts { get; init; }

    public int PlantAborts { get; init; }

    public int DefuseAttempts { get; init; }

    public int DefuseAborts { get; init; }

    // Defused with less than RoundAnalyzer.ClutchDefuseMs left on the bomb.
    public bool ClutchDefuse { get; init; }

    // Health lost this round, from any source (enemies, teammates, world, bots).
    public int DamageTaken { get; init; }

    public int BombCarryMs { get; init; }

    // Kills counted in Kills that were made without a scope, through at least one wall, through smoke, while blind or in
    // the air; one kill can count in several.
    public int NoscopeKills { get; init; }

    public int WallbangKills { get; init; }

    public int SmokeKills { get; init; }

    public int BlindKills { get; init; }

    public int AirKills { get; init; }

    // 0 for a dead player or a leaver; null only for a living player without a state read at round_end.
    public int? HealthEnd { get; init; }

    public int? ArmorEnd { get; init; }

    // Null without a live phase (no freeze end seen) or with the sampling off.
    public MovementRoundStats? Movement { get; init; }

    public IReadOnlyList<WeaponRoundStats> Weapons { get; init; } = Array.Empty<WeaponRoundStats>();
}

public sealed record GrenadeSummary(
    ulong Thrower, GrenadeType Type, int? ThrownMs, int? DetonatedMs, Vec3? ThrowPosition, Vec3? DetonationPosition,
    int Damage, int EnemiesBlinded, int TeammatesBlinded, double BlindSeconds);

public sealed record RoundSummary(RoundRecord Record, IReadOnlyList<PlayerRoundStats> Players, IReadOnlyList<GrenadeSummary> Grenades)
{
    // The samples only feed the analysis: a summary waiting in the write queue (minutes during a database outage) must
    // not hold them.
    public RoundSummary WithoutSamples() => this with { Record = Record with { Samples = Array.Empty<MovementSample>() } };
}

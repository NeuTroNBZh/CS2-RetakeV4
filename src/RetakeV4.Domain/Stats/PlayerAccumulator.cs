using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Stats;

internal sealed class WeaponAccumulator(string weapon)
{
    public string Weapon { get; } = weapon;

    public int Shots { get; set; }

    public int Hits { get; set; }

    public int HeadshotHits { get; set; }

    public int Kills { get; set; }

    public int HeadshotKills { get; set; }

    public int Damage { get; set; }

    public WeaponRoundStats Build() => new(Weapon, Shots, Hits, HeadshotHits, Kills, HeadshotKills, Damage);
}

internal sealed class PlayerAccumulator(RoundParticipant participant)
{
    private readonly Dictionary<string, WeaponAccumulator> _weapons = new(StringComparer.Ordinal);

    public int Kills { get; set; }

    public int Deaths { get; set; }

    public int Assists { get; set; }

    public int FlashAssists { get; set; }

    public int TeamKills { get; set; }

    public int Damage { get; set; }

    public int UtilityDamage { get; set; }

    public int HeadshotKills { get; set; }

    public int Shots { get; set; }

    public int Hits { get; set; }

    public int HeadshotHits { get; set; }

    public bool EntryKill { get; set; }

    public bool EntryDeath { get; set; }

    public int TradeKills { get; set; }

    public bool TradedDeath { get; set; }

    public int ClutchVs { get; set; }

    public int EnemiesFlashed { get; set; }

    public int TeammatesFlashed { get; set; }

    public double BlindSeconds { get; set; }

    public bool Mvp { get; set; }

    public bool Planted { get; set; }

    public bool Defused { get; set; }

    public bool DefusedWithKit { get; set; }

    public int ShotsStill { get; set; }

    public int HitsStill { get; set; }

    public int ShotsMoving { get; set; }

    public int HitsMoving { get; set; }

    public int ShotsScoped { get; set; }

    public int HitsScoped { get; set; }

    public int Reloads { get; set; }

    public int ReloadsFull { get; set; }

    public int Pickups { get; set; }

    public int Drops { get; set; }

    public int Jumps { get; set; }

    public int PlantAttempts { get; set; }

    public int PlantAborts { get; set; }

    public int DefuseAttempts { get; set; }

    public int DefuseAborts { get; set; }

    public bool ClutchDefuse { get; set; }

    public int DamageTaken { get; set; }

    public int BombCarryMs { get; set; }

    public int? HealthEnd { get; set; }

    public int? ArmorEnd { get; set; }

    public MovementRoundStats? Movement { get; set; }

    public WeaponAccumulator Weapon(string weapon)
    {
        var key = StatsWeapons.Normalize(weapon);
        if (!_weapons.TryGetValue(key, out var accumulator))
        {
            accumulator = new WeaponAccumulator(key);
            _weapons[key] = accumulator;
        }
        return accumulator;
    }

    public PlayerRoundStats Build(TeamSide? winner, bool dead)
    {
        var won = winner == participant.Side;
        return new PlayerRoundStats(participant.SteamId, participant.Name, participant.Side)
        {
            PrimaryWeapon = participant.PrimaryWeapon,
            Survived = !dead,
            Won = won,
            Kills = Kills,
            Deaths = Deaths,
            Assists = Assists,
            FlashAssists = FlashAssists,
            TeamKills = TeamKills,
            Damage = Damage,
            UtilityDamage = UtilityDamage,
            HeadshotKills = HeadshotKills,
            Shots = Shots,
            Hits = Hits,
            HeadshotHits = HeadshotHits,
            EntryKill = EntryKill,
            EntryDeath = EntryDeath,
            TradeKills = TradeKills,
            TradedDeath = TradedDeath,
            Kast = Kills > 0 || Assists > 0 || !dead || TradedDeath,
            ClutchVs = ClutchVs,
            ClutchWon = ClutchVs > 0 && won,
            EnemiesFlashed = EnemiesFlashed,
            TeammatesFlashed = TeammatesFlashed,
            BlindSecondsInflicted = Math.Round(BlindSeconds, 3),
            Mvp = Mvp,
            Planted = Planted,
            Defused = Defused,
            DefusedWithKit = DefusedWithKit,
            EloBefore = participant.EloBefore,
            EloAfter = participant.EloBefore,
            ShotsStill = ShotsStill,
            HitsStill = HitsStill,
            ShotsMoving = ShotsMoving,
            HitsMoving = HitsMoving,
            ShotsScoped = ShotsScoped,
            HitsScoped = HitsScoped,
            Reloads = Reloads,
            ReloadsFull = ReloadsFull,
            Pickups = Pickups,
            Drops = Drops,
            Jumps = Jumps,
            PlantAttempts = PlantAttempts,
            PlantAborts = PlantAborts,
            DefuseAttempts = DefuseAttempts,
            DefuseAborts = DefuseAborts,
            ClutchDefuse = ClutchDefuse,
            DamageTaken = DamageTaken,
            BombCarryMs = BombCarryMs,
            HealthEnd = HealthEnd,
            ArmorEnd = ArmorEnd,
            Movement = Movement,
            Weapons = _weapons.Values.OrderBy(w => w.Weapon, StringComparer.Ordinal).Select(w => w.Build()).ToList(),
        };
    }
}

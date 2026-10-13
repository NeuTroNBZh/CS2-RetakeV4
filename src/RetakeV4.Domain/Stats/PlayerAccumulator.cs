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
            Weapons = _weapons.Values.OrderBy(w => w.Weapon, StringComparer.Ordinal).Select(w => w.Build()).ToList(),
        };
    }
}

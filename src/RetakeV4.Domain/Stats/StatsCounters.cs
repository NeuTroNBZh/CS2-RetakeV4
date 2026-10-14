using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Stats;

// Additive counters stored per (player, scope). Columns are the v1 counters, frozen with views.v1.json and
// StatsSchema.V1Counters; V2Columns were added in 4.9 by ALTER migrations (StatsSchema.V2Counters) and appear in the v2
// views only. The order of each list is the order of the values returned by For / ForV2 and of the view columns.
// A new counter needs a new list, an ALTER migration appended to StatsSchema.Migrations and a new contract version.
public static class StatsCounters
{
    public static IReadOnlyList<string> Columns { get; } = BuildColumns();

    public static IReadOnlyList<string> WeaponColumns { get; } = new[] { "shots", "hits", "headshot_hits", "kills", "headshot_kills", "damage" };

    public static IReadOnlyList<string> V2Columns { get; } = new[]
    {
        "shots_still", "hits_still", "shots_moving", "hits_moving", "shots_scoped", "hits_scoped",
        "reloads", "reloads_full", "pickups", "drops", "jumps", "plant_attempts", "plant_aborts", "defuse_attempts", "defuse_aborts",
        "clutch_defuses", "damage_taken", "bomb_carry_ms",
    };

    public static IReadOnlyList<string> AllColumns { get; } = Columns.Concat(V2Columns).ToArray();

    public static long[] For(PlayerRoundStats player, bool ranked)
    {
        ArgumentNullException.ThrowIfNull(player);
        var clutch = Math.Min(player.ClutchVs, 5);
        var values = new List<long>
        {
            1, B(player.Won), B(ranked), B(player.Survived),
            player.Kills, player.Deaths, player.Assists, player.FlashAssists, player.TeamKills,
            player.Damage, player.UtilityDamage, player.HeadshotKills, player.Shots, player.Hits, player.HeadshotHits,
            B(player.EntryKill), B(player.EntryDeath), player.TradeKills, B(player.TradedDeath), B(player.Kast),
            B(player.Kills == 2), B(player.Kills == 3), B(player.Kills == 4), B(player.Kills >= 5),
        };
        for (var x = 1; x <= 5; x++)
        {
            values.Add(B(clutch == x));
            values.Add(B(clutch == x && player.ClutchWon));
        }
        values.Add(player.EnemiesFlashed);
        values.Add(player.TeammatesFlashed);
        values.Add((long)Math.Round(player.BlindSecondsInflicted * 1000, MidpointRounding.AwayFromZero));
        values.Add(B(player.Mvp));
        values.Add(B(player.Planted));
        values.Add(B(player.Defused));
        values.Add(B(player.Defused && player.DefusedWithKit));
        values.Add((long)Math.Round(player.Rating * 1000, MidpointRounding.AwayFromZero));
        return values.ToArray();
    }

    public static long[] ForV2(PlayerRoundStats player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new long[]
        {
            player.ShotsStill, player.HitsStill, player.ShotsMoving, player.HitsMoving, player.ShotsScoped, player.HitsScoped,
            player.Reloads, player.ReloadsFull, player.Pickups, player.Drops, player.Jumps, player.PlantAttempts, player.PlantAborts,
            player.DefuseAttempts, player.DefuseAborts, B(player.ClutchDefuse), player.DamageTaken, player.BombCarryMs,
        };
    }

    public static long[] ForAll(PlayerRoundStats player, bool ranked) => For(player, ranked).Concat(ForV2(player)).ToArray();

    public static long[] ForWeapon(WeaponRoundStats weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return new long[] { weapon.Shots, weapon.Hits, weapon.HeadshotHits, weapon.Kills, weapon.HeadshotKills, weapon.Damage };
    }

    public static IReadOnlyList<string> Scopes(RoundContext context, TeamSide side)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scopes = new List<string> { "all", $"map:{context.MapName}", $"side:{side}" };
        if (context.Site is { } site)
        {
            scopes.Add($"site:{site}");
        }
        if (!string.IsNullOrWhiteSpace(context.RoundType))
        {
            scopes.Add($"roundtype:{context.RoundType}");
        }
        return scopes;
    }

    private static long B(bool value) => value ? 1 : 0;

    private static IReadOnlyList<string> BuildColumns()
    {
        var columns = new List<string>
        {
            "rounds", "rounds_won", "ranked_rounds", "survived_rounds",
            "kills", "deaths", "assists", "flash_assists", "team_kills",
            "damage", "utility_damage", "headshot_kills", "shots", "hits", "headshot_hits",
            "entry_kills", "entry_deaths", "trade_kills", "traded_deaths", "kast_rounds",
            "k2", "k3", "k4", "k5",
        };
        for (var x = 1; x <= 5; x++)
        {
            columns.Add($"clutch_{x}_played");
            columns.Add($"clutch_{x}_won");
        }
        columns.AddRange(new[] { "enemies_flashed", "teammates_flashed", "blind_ms", "mvps", "plants", "defuses", "defuses_with_kit", "rating_milli" });
        return columns;
    }
}

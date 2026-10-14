using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class StatsCountersTests
{
    private static long Value(long[] values, string column) => values[StatsCounters.Columns.ToList().IndexOf(column)];

    [Fact]
    public void Columns_AreDistinct_AndMatchTheValues()
    {
        Assert.Equal(42, StatsCounters.Columns.Count);
        Assert.Equal(StatsCounters.Columns.Count, StatsCounters.Columns.Distinct().Count());
        Assert.Equal(StatsCounters.Columns.Count, StatsCounters.For(new PlayerRoundStats(1, "a", TeamSide.CT), false).Length);
    }

    // Every counted field has its own value (booleans alternate where possible), so that swapping two fields fails.
    [Fact]
    public void For_MapsEveryCounter()
    {
        var player = new PlayerRoundStats(1, "a", TeamSide.CT)
        {
            Won = false, Survived = false, Kills = 4, Deaths = 5, Assists = 6, FlashAssists = 7, TeamKills = 8,
            Damage = 309, UtilityDamage = 41, HeadshotKills = 3, Shots = 33, Hits = 13, HeadshotHits = 9,
            EntryKill = true, EntryDeath = false, TradeKills = 2, TradedDeath = true, Kast = false, ClutchVs = 3, ClutchWon = false,
            EnemiesFlashed = 10, TeammatesFlashed = 11, BlindSecondsInflicted = 1.234, Mvp = true, Planted = false, Defused = true,
            DefusedWithKit = false, Rating = 1.4567,
        };
        var expected = new long[]
        {
            1, 0, 1, 0,                         // rounds, rounds_won, ranked_rounds, survived_rounds
            4, 5, 6, 7, 8,                      // kills, deaths, assists, flash_assists, team_kills
            309, 41, 3, 33, 13, 9,              // damage, utility_damage, headshot_kills, shots, hits, headshot_hits
            1, 0, 2, 1, 0,                      // entry_kills, entry_deaths, trade_kills, traded_deaths, kast_rounds
            0, 0, 1, 0,                         // k2, k3, k4, k5
            0, 0, 0, 0, 1, 0, 0, 0, 0, 0,       // clutch_1..5 played/won
            10, 11, 1234, 1, 0, 1, 0, 1457,     // enemies_flashed .. rating_milli
        };
        Assert.Equal(expected, StatsCounters.For(player, ranked: true));
    }

    [Fact]
    public void DefuseWithKit_CountsOnlyWhenDefused()
    {
        var values = StatsCounters.For(new PlayerRoundStats(1, "a", TeamSide.CT) { Defused = true, DefusedWithKit = true, ClutchVs = 7, ClutchWon = true }, ranked: false);
        Assert.Equal(1, Value(values, "defuses_with_kit"));
        Assert.Equal(1, Value(values, "clutch_5_played"));
        Assert.Equal(1, Value(values, "clutch_5_won"));
        Assert.Equal(0, Value(StatsCounters.For(new PlayerRoundStats(1, "a", TeamSide.CT) { DefusedWithKit = true }, ranked: false), "defuses_with_kit"));
    }

    [Fact]
    public void LostClutch_CountsPlayedNotWon()
    {
        var values = StatsCounters.For(new PlayerRoundStats(1, "a", TeamSide.T) { ClutchVs = 2 }, ranked: false);
        Assert.Equal(1, Value(values, "clutch_2_played"));
        Assert.Equal(0, Value(values, "clutch_2_won"));
        Assert.Equal(0, Value(values, "ranked_rounds"));
    }

    [Fact]
    public void Scopes_CoverMapSideSiteAndRoundType()
    {
        var scopes = StatsCounters.Scopes(Context(), TeamSide.CT);
        Assert.Equal(new[] { "all", "map:de_mirage", "side:CT", "site:A", "roundtype:FullBuy" }, scopes);
    }

    [Fact]
    public void Scopes_SkipUnknownSiteAndRoundType()
    {
        var scopes = StatsCounters.Scopes(Context() with { Site = null, RoundType = null }, TeamSide.T);
        Assert.Equal(new[] { "all", "map:de_mirage", "side:T" }, scopes);
    }

    [Fact]
    public void ForWeapon_FollowsWeaponColumns()
    {
        Assert.Equal(new[] { "shots", "hits", "headshot_hits", "kills", "headshot_kills", "damage" }, StatsCounters.WeaponColumns);
        Assert.Equal(new long[] { 10, 4, 1, 2, 1, 150 }, StatsCounters.ForWeapon(new WeaponRoundStats("ak47", 10, 4, 1, 2, 1, 150)));
    }
}

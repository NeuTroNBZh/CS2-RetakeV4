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

    [Fact]
    public void For_MapsEveryCounter()
    {
        var player = new PlayerRoundStats(1, "a", TeamSide.CT)
        {
            Won = true, Survived = true, Kills = 3, Assists = 1, FlashAssists = 1, Damage = 250, UtilityDamage = 40, HeadshotKills = 2,
            Shots = 30, Hits = 10, HeadshotHits = 4, EntryKill = true, TradeKills = 1, Kast = true, ClutchVs = 7, ClutchWon = true,
            EnemiesFlashed = 2, TeammatesFlashed = 1, BlindSecondsInflicted = 1.25, Mvp = true, Defused = true, DefusedWithKit = true, Rating = 1.4567,
        };
        var values = StatsCounters.For(player, ranked: true);
        Assert.Equal(1, Value(values, "rounds"));
        Assert.Equal(1, Value(values, "rounds_won"));
        Assert.Equal(1, Value(values, "ranked_rounds"));
        Assert.Equal(1, Value(values, "survived_rounds"));
        Assert.Equal(3, Value(values, "kills"));
        Assert.Equal(0, Value(values, "k2"));
        Assert.Equal(1, Value(values, "k3"));
        Assert.Equal(250, Value(values, "damage"));
        Assert.Equal(1, Value(values, "entry_kills"));
        Assert.Equal(1, Value(values, "kast_rounds"));
        Assert.Equal(1, Value(values, "clutch_5_played"));
        Assert.Equal(1, Value(values, "clutch_5_won"));
        Assert.Equal(0, Value(values, "clutch_1_played"));
        Assert.Equal(1250, Value(values, "blind_ms"));
        Assert.Equal(1, Value(values, "mvps"));
        Assert.Equal(1, Value(values, "defuses"));
        Assert.Equal(1, Value(values, "defuses_with_kit"));
        Assert.Equal(1457, Value(values, "rating_milli"));
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

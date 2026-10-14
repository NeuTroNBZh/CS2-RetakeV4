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

    [Fact]
    public void V2Columns_FollowTheV1Columns()
    {
        Assert.Equal(
            new[]
            {
                "shots_still", "hits_still", "shots_moving", "hits_moving", "shots_scoped", "hits_scoped",
                "reloads", "reloads_full", "pickups", "drops", "jumps", "plant_attempts", "plant_aborts", "defuse_attempts", "defuse_aborts",
                "clutch_defuses", "damage_taken", "bomb_carry_ms",
            },
            StatsCounters.V2Columns);
        Assert.Equal(
            StatsCounters.Columns.Concat(StatsCounters.V2Columns).Concat(StatsCounters.MovementColumns).Concat(StatsCounters.SpecialKillColumns),
            StatsCounters.AllColumns);
        Assert.Equal(StatsCounters.AllColumns.Count, StatsCounters.AllColumns.Distinct().Count());
        Assert.Equal(81, StatsCounters.ForAll(new PlayerRoundStats(1, "a", TeamSide.CT), false).Length);
    }

    [Fact]
    public void ForV2_MapsEveryCounter()
    {
        var player = new PlayerRoundStats(1, "a", TeamSide.CT)
        {
            ShotsStill = 21, HitsStill = 22, ShotsMoving = 23, HitsMoving = 24, ShotsScoped = 25, HitsScoped = 26,
            Reloads = 27, ReloadsFull = 28, Pickups = 29, Drops = 30, Jumps = 31, PlantAttempts = 32, PlantAborts = 33,
            DefuseAttempts = 34, DefuseAborts = 35, ClutchDefuse = true, DamageTaken = 37, BombCarryMs = 38,
        };
        Assert.Equal(new long[] { 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 1, 37, 38 }, StatsCounters.ForV2(player));
        Assert.Equal(
            StatsCounters.For(player, true).Concat(StatsCounters.ForV2(player)).Concat(StatsCounters.ForMovement(player))
                .Concat(StatsCounters.ForSpecialKills(player)),
            StatsCounters.ForAll(player, true));
        Assert.Equal(0, StatsCounters.ForV2(player with { ClutchDefuse = false })[15]);
    }

    [Fact]
    public void MovementColumns_AreTheSpecAggregates()
    {
        Assert.Equal(
            new[]
            {
                "alive_ms", "walk_samples", "run_samples", "crouch_samples", "still_samples", "air_samples", "alive_samples",
                "distance_cm", "afk_ms", "in_smoke_samples", "behind_smoke_samples", "first_contact_ms_sum", "first_contact_rounds",
                "engage_order_sum", "bait_rounds", "bait_chances",
            },
            StatsCounters.MovementColumns);
        Assert.Empty(StatsCounters.MovementColumns.Intersect(StatsCounters.Columns.Concat(StatsCounters.V2Columns)));
    }

    [Fact]
    public void ForMovement_MapsEveryCounter()
    {
        var movement = new MovementRoundStats
        {
            AliveMs = 29000, WalkSamples = 2, RunSamples = 3, CrouchSamples = 4, StillSamples = 5, AirSamples = 6, AliveSamples = 20,
            DistanceUnits = 250, AfkMs = 15000, InSmokeSamples = 7, BehindSmokeSamples = 8, FirstContactMs = 500, EngageOrder = 2,
            BaitChance = true, Bait = true,
        };
        var player = new PlayerRoundStats(1, "a", TeamSide.CT) { Movement = movement };
        Assert.Equal(new long[] { 29000, 2, 3, 4, 5, 6, 20, 635, 15000, 7, 8, 500, 1, 2, 1, 1 }, StatsCounters.ForMovement(player));
        var quiet = player with { Movement = movement with { FirstContactMs = null, EngageOrder = null, Bait = false } };
        Assert.Equal(new long[] { 29000, 2, 3, 4, 5, 6, 20, 635, 15000, 7, 8, 0, 0, 0, 0, 1 }, StatsCounters.ForMovement(quiet));
        Assert.Equal(new long[16], StatsCounters.ForMovement(new PlayerRoundStats(1, "a", TeamSide.CT)));
    }

    [Fact]
    public void SpecialKillColumns_FollowTheMovementColumns()
    {
        Assert.Equal(new[] { "noscope_kills", "wallbang_kills", "smoke_kills", "blind_kills", "air_kills" }, StatsCounters.SpecialKillColumns);
        Assert.Empty(StatsCounters.SpecialKillColumns.Intersect(StatsCounters.Columns.Concat(StatsCounters.V2Columns).Concat(StatsCounters.MovementColumns)));
    }

    [Fact]
    public void ForSpecialKills_MapsEveryCounter()
    {
        var player = new PlayerRoundStats(1, "a", TeamSide.CT) { NoscopeKills = 1, WallbangKills = 2, SmokeKills = 3, BlindKills = 4, AirKills = 5 };
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, StatsCounters.ForSpecialKills(player));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, StatsCounters.ForAll(player, false).TakeLast(5));
        Assert.Equal(new long[5], StatsCounters.ForSpecialKills(new PlayerRoundStats(1, "a", TeamSide.CT)));
    }
}

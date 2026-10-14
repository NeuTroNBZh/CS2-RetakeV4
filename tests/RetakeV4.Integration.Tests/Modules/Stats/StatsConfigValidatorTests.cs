using RetakeV4.Configuration;
using RetakeV4.Domain.Stats;
using RetakeV4.Modules.Stats;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Modules.Stats;

public class StatsConfigValidatorTests
{
    private static (StatsConfig Config, IReadOnlyList<ConfigIssue> Issues) Validate(StatsConfig config)
    {
        var result = new StatsConfigValidator().Validate(config, new StatsConfig(), "stats.json");
        return (result.Config, result.Issues);
    }

    [Fact]
    public void Defaults_AreValid()
    {
        var (config, issues) = Validate(new StatsConfig());
        Assert.Empty(issues);
        Assert.Equal(new StatsConfig().ToRules(), config.ToRules());
    }

    [Fact]
    public void Defaults_MatchTheSpec()
    {
        var rules = new StatsConfig().ToRules();
        Assert.Equal(5.0, rules.TradeWindowSeconds);
        Assert.Equal(1, rules.MinPlayersPerSide);
        Assert.Equal(20000, rules.MaxShotsPerRound);
        Assert.Equal(new RatingWeights(0.30, 0.35, 0.20, 0.004, 0.15, 0.20), rules.Rating);
        Assert.Equal(new EloRules(1000, 100, 24, 50), rules.Elo);
        Assert.Equal(30, new StatsConfig().RawRetentionDays);
        Assert.Equal(200, new StatsConfig().QueueCapacity);
        Assert.Equal(5, new StatsConfig().ShutdownFlushSeconds);
        Assert.False(new StatsConfig().ScoreboardElo);
    }

    [Fact]
    public void ScoreboardElo_CanBeEnabled()
    {
        var (config, issues) = Validate(new StatsConfig { ScoreboardElo = true });
        Assert.True(config.ScoreboardElo);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(3651, 30)]
    [InlineData(0, 0)]
    public void RawRetentionDays_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { RawRetentionDays = value });
        Assert.Equal(expected, config.RawRetentionDays);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Fact]
    public void OutOfRangeValues_FallBackToDefaults_WithOneIssueEach()
    {
        var (config, issues) = Validate(new StatsConfig
        {
            MaxShotsPerRound = 10,
            MinPlayersPerSide = 0,
            TradeWindowSeconds = double.NaN,
            QueueCapacity = 1,
            ShutdownFlushSeconds = 120,
        });
        Assert.Equal(20000, config.MaxShotsPerRound);
        Assert.Equal(1, config.MinPlayersPerSide);
        Assert.Equal(5.0, config.TradeWindowSeconds);
        Assert.Equal(200, config.QueueCapacity);
        Assert.Equal(5, config.ShutdownFlushSeconds);
        Assert.Equal(5, issues.Count);
    }

    [Fact]
    public void InvalidRatingWeight_FallsBack()
    {
        var (config, issues) = Validate(new StatsConfig { Rating = new StatsRatingConfig { Kill = -1, Damage = double.PositiveInfinity } });
        Assert.Equal(0.35, config.Rating.Kill);
        Assert.Equal(0.004, config.Rating.Damage);
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Key == "Rating.Kill");
    }

    [Fact]
    public void InvalidElo_FallsBack()
    {
        var (config, issues) = Validate(new StatsConfig { Elo = new StatsEloConfig { Start = 50, K = 0, Floor = 2000, PlayerAdvantage = -5 } });
        Assert.Equal(new StatsEloConfig(), config.Elo);
        Assert.Equal(4, issues.Count);
    }

    [Fact]
    public void MissingBlocks_UseDefaults()
    {
        var (config, issues) = Validate(new StatsConfig { Database = null!, Rating = null!, Elo = null! });
        Assert.Equal(new DatabaseConfig(), config.Database);
        Assert.Equal(new StatsRatingConfig(), config.Rating);
        Assert.Equal(new StatsEloConfig(), config.Elo);
        Assert.Equal(3, issues.Count);
    }

    [Fact]
    public void MySqlWithoutConnectionString_FallsBackToSqlite()
    {
        var (config, issues) = Validate(new StatsConfig { Database = new DatabaseConfig { Type = DatabaseType.MySql } });
        Assert.Equal(DatabaseType.Sqlite, config.Database.Type);
        Assert.Contains(issues, i => i.Key == "Database.MySqlConnectionString");
    }

    [Fact]
    public void EnrichedEventDefaults_MatchTheSpec()
    {
        var config = new StatsConfig();
        Assert.Equal(60, config.PresenceSnapshotSeconds);
        Assert.Equal(30, config.PingSampleSeconds);
        Assert.Equal(50, config.LongFrameMs);
        Assert.Equal("geo/dbip-country-lite.mmdb", config.GeoDatabaseFile);
        Assert.True(config.TrackServerHealth);
        Assert.True(config.TrackUsage);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 60)]
    [InlineData(10, 10)]
    [InlineData(3600, 3600)]
    [InlineData(3601, 60)]
    public void PresenceSnapshotSeconds_IsOffOrBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { PresenceSnapshotSeconds = value });
        Assert.Equal(expected, config.PresenceSnapshotSeconds);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 30)]
    [InlineData(600, 600)]
    [InlineData(601, 30)]
    public void PingSampleSeconds_IsOffOrBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { PingSampleSeconds = value });
        Assert.Equal(expected, config.PingSampleSeconds);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(19, 50)]
    [InlineData(20, 20)]
    [InlineData(5000, 5000)]
    [InlineData(5001, 50)]
    public void LongFrameMs_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { LongFrameMs = value });
        Assert.Equal(expected, config.LongFrameMs);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData("geo/custom.mmdb", true)]
    [InlineData("dbip.mmdb", true)]
    [InlineData("../dbip.mmdb", false)]
    [InlineData("geo/../dbip.mmdb", false)]
    [InlineData("/etc/dbip.mmdb", false)]
    [InlineData("geo/dbip.db", false)]
    [InlineData("", false)]
    public void GeoDatabaseFile_MustBeARelativeMmdbPath(string value, bool valid)
    {
        var (config, issues) = Validate(new StatsConfig { GeoDatabaseFile = value });
        Assert.Equal(valid ? value : "geo/dbip-country-lite.mmdb", config.GeoDatabaseFile);
        Assert.Equal(valid ? 0 : 1, issues.Count);
    }

    [Fact]
    public void AnOperatorsStatsJson_IsRead()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("stats.json"), """
            { "Version": 1, "PresenceSnapshotSeconds": 120, "PingSampleSeconds": 0, "LongFrameMs": 80,
              "GeoDatabaseFile": "geo/custom.mmdb", "TrackServerHealth": false, "TrackUsage": false }
            """);
        var config = new JsonConfigStore(dir.Path).Load("stats.json", new StatsConfig(), new StatsConfigValidator()).Config;
        Assert.Equal(120, config.PresenceSnapshotSeconds);
        Assert.Equal(0, config.PingSampleSeconds);
        Assert.Equal(80, config.LongFrameMs);
        Assert.Equal("geo/custom.mmdb", config.GeoDatabaseFile);
        Assert.False(config.TrackServerHealth);
        Assert.False(config.TrackUsage);
    }

    [Fact]
    public void MovementDefaults_MatchTheSpec()
    {
        var config = new StatsConfig();
        Assert.Equal(8, config.SampleEveryTicks);
        Assert.Equal(72, config.SmokeRadius);
        Assert.Equal(394, config.BaitDistance);
        Assert.Equal(5.0, config.BaitWindowSeconds);
        Assert.Equal(15, config.AfkSeconds);
        Assert.Equal(new MovementRules(72f, 394f, 5.0, 15.0), config.ToRules().Movement);
        Assert.Null((config with { SampleEveryTicks = 0 }).ToRules().Movement);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(64, 64)]
    [InlineData(65, 8)]
    [InlineData(-1, 8)]
    public void SampleEveryTicks_IsOffOrBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { SampleEveryTicks = value });
        Assert.Equal(expected, config.SampleEveryTicks);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(15, 72)]
    [InlineData(16, 16)]
    [InlineData(256, 256)]
    [InlineData(257, 72)]
    public void SmokeRadius_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { SmokeRadius = value });
        Assert.Equal(expected, config.SmokeRadius);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(49, 394)]
    [InlineData(50, 50)]
    [InlineData(2000, 2000)]
    [InlineData(2001, 394)]
    public void BaitDistance_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { BaitDistance = value });
        Assert.Equal(expected, config.BaitDistance);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(0.4, 5.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(30.0, 30.0)]
    [InlineData(30.5, 5.0)]
    [InlineData(double.NaN, 5.0)]
    public void BaitWindowSeconds_IsBounded(double value, double expected)
    {
        var (config, issues) = Validate(new StatsConfig { BaitWindowSeconds = value });
        Assert.Equal(expected, config.BaitWindowSeconds);
        Assert.Equal(value.Equals(expected) ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(4, 15)]
    [InlineData(5, 5)]
    [InlineData(300, 300)]
    [InlineData(301, 15)]
    public void AfkSeconds_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { AfkSeconds = value });
        Assert.Equal(expected, config.AfkSeconds);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Fact]
    public void AnOperatorsStatsJson_ReadsTheMovementKeys()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("stats.json"), """
            { "Version": 1, "SampleEveryTicks": 16, "SmokeRadius": 80, "BaitDistance": 300, "BaitWindowSeconds": 3.5, "AfkSeconds": 20 }
            """);
        var config = new JsonConfigStore(dir.Path).Load("stats.json", new StatsConfig(), new StatsConfigValidator()).Config;
        Assert.Equal(new MovementRules(80f, 300f, 3.5, 20.0), config.ToRules().Movement);
        Assert.Equal(16, config.SampleEveryTicks);
    }

    [Fact]
    public void ReplayDefaults_MatchTheSpec()
    {
        var config = new StatsConfig();
        Assert.Equal(10, config.ReplayBestPerPlayer);
        Assert.Equal(10, config.ReplayHighlightsPerPlayer);
        Assert.Equal(20, config.ReplaySavedPerPlayer);
        Assert.Equal(5, config.ReplayMemoryRounds);
        Assert.Equal(new ReplayCaps(10, 10, 20), config.ToReplayCaps());
        Assert.False((config with { ReplayBestPerPlayer = 0, ReplayHighlightsPerPlayer = 0, ReplaySavedPerPlayer = 0 }).ToReplayCaps().Any);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(101, 10)]
    public void ReplayBestPerPlayer_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { ReplayBestPerPlayer = value });
        Assert.Equal(expected, config.ReplayBestPerPlayer);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(101, 10)]
    public void ReplayHighlightsPerPlayer_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { ReplayHighlightsPerPlayer = value });
        Assert.Equal(expected, config.ReplayHighlightsPerPlayer);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(101, 20)]
    public void ReplaySavedPerPlayer_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { ReplaySavedPerPlayer = value });
        Assert.Equal(expected, config.ReplaySavedPerPlayer);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(11, 5)]
    public void ReplayMemoryRounds_IsBounded(int value, int expected)
    {
        var (config, issues) = Validate(new StatsConfig { ReplayMemoryRounds = value });
        Assert.Equal(expected, config.ReplayMemoryRounds);
        Assert.Equal(value == expected ? 0 : 1, issues.Count);
    }

    [Fact]
    public void AnOperatorsStatsJson_ReadsTheReplayKeys()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("stats.json"), """
            { "Version": 1, "ReplayBestPerPlayer": 3, "ReplayHighlightsPerPlayer": 0, "ReplaySavedPerPlayer": 5, "ReplayMemoryRounds": 2 }
            """);
        var config = new JsonConfigStore(dir.Path).Load("stats.json", new StatsConfig(), new StatsConfigValidator()).Config;
        Assert.Equal(new ReplayCaps(3, 0, 5), config.ToReplayCaps());
        Assert.Equal(2, config.ReplayMemoryRounds);
    }
}

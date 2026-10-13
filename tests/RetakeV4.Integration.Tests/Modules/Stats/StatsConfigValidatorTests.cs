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
}

using System.Globalization;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Stats;

public sealed class StatsConfigValidator : IConfigValidator<StatsConfig>
{
    private const double MaxWeight = 10;

    public ValidationResult<StatsConfig> Validate(StatsConfig config, StatsConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var database = DatabaseConfigValidator.Validate(config.Database, defaults.Database, file, nameof(StatsConfig.Database), issues);
        var rating = config.Rating ?? Missing(defaults.Rating, file, nameof(StatsConfig.Rating), issues);
        var elo = config.Elo ?? Missing(defaults.Elo, file, nameof(StatsConfig.Elo), issues);
        var validated = config with
        {
            Database = database,
            RawRetentionDays = Range(config.RawRetentionDays, 0, 3650, defaults.RawRetentionDays, file, nameof(StatsConfig.RawRetentionDays), issues),
            MaxShotsPerRound = Range(config.MaxShotsPerRound, 100, 1_000_000, defaults.MaxShotsPerRound, file, nameof(StatsConfig.MaxShotsPerRound), issues),
            MinPlayersPerSide = Range(config.MinPlayersPerSide, 1, 5, defaults.MinPlayersPerSide, file, nameof(StatsConfig.MinPlayersPerSide), issues),
            TradeWindowSeconds = Range(config.TradeWindowSeconds, 0.5, 30, defaults.TradeWindowSeconds, file, nameof(StatsConfig.TradeWindowSeconds), issues),
            QueueCapacity = Range(config.QueueCapacity, 10, 10_000, defaults.QueueCapacity, file, nameof(StatsConfig.QueueCapacity), issues),
            ShutdownFlushSeconds = Range(config.ShutdownFlushSeconds, 0, 60, defaults.ShutdownFlushSeconds, file, nameof(StatsConfig.ShutdownFlushSeconds), issues),
            Rating = ValidRating(rating, defaults.Rating, file, issues),
            Elo = ValidElo(elo, defaults.Elo, file, issues),
        };
        return new ValidationResult<StatsConfig>(validated, issues);
    }

    private static StatsRatingConfig ValidRating(StatsRatingConfig rating, StatsRatingConfig defaults, string file, List<ConfigIssue> issues) => rating with
    {
        Base = Weight(rating.Base, defaults.Base, file, nameof(StatsRatingConfig.Base), issues),
        Kill = Weight(rating.Kill, defaults.Kill, file, nameof(StatsRatingConfig.Kill), issues),
        Survive = Weight(rating.Survive, defaults.Survive, file, nameof(StatsRatingConfig.Survive), issues),
        Damage = Weight(rating.Damage, defaults.Damage, file, nameof(StatsRatingConfig.Damage), issues),
        Kast = Weight(rating.Kast, defaults.Kast, file, nameof(StatsRatingConfig.Kast), issues),
        Impact = Weight(rating.Impact, defaults.Impact, file, nameof(StatsRatingConfig.Impact), issues),
    };

    private static StatsEloConfig ValidElo(StatsEloConfig elo, StatsEloConfig defaults, string file, List<ConfigIssue> issues)
    {
        var start = Range(elo.Start, 100, 5000, defaults.Start, file, "Elo.Start", issues);
        return elo with
        {
            Start = start,
            Floor = Range(elo.Floor, 0, start, defaults.Floor, file, "Elo.Floor", issues),
            K = Range(elo.K, 1, 200, defaults.K, file, "Elo.K", issues),
            PlayerAdvantage = Range(elo.PlayerAdvantage, 0, 400, defaults.PlayerAdvantage, file, "Elo.PlayerAdvantage", issues),
        };
    }

    private static double Weight(double value, double fallback, string file, string name, List<ConfigIssue> issues) =>
        Range(value, 0, MaxWeight, fallback, file, $"Rating.{name}", issues);

    private static int Range(int value, int min, int max, int fallback, string file, string key, List<ConfigIssue> issues)
    {
        if (value >= min && value <= max)
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, string.Create(CultureInfo.InvariantCulture, $"must be between {min} and {max}; using default")));
        return fallback;
    }

    private static double Range(double value, double min, double max, double fallback, string file, string key, List<ConfigIssue> issues)
    {
        if (double.IsFinite(value) && value >= min && value <= max)
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, string.Create(CultureInfo.InvariantCulture, $"must be between {min} and {max}; using default")));
        return fallback;
    }

    private static T Missing<T>(T defaults, string file, string key, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, key, "missing; using defaults"));
        return defaults;
    }
}

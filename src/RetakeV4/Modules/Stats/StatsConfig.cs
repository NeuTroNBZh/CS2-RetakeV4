using RetakeV4.Configuration;
using RetakeV4.Domain.Stats;
using RetakeV4.Persistence;

namespace RetakeV4.Modules.Stats;

public sealed record StatsRatingConfig
{
    public double Base { get; init; } = 0.30;

    public double Kill { get; init; } = 0.35;

    public double Survive { get; init; } = 0.20;

    public double Damage { get; init; } = 0.004;

    public double Kast { get; init; } = 0.15;

    public double Impact { get; init; } = 0.20;
}

public sealed record StatsEloConfig
{
    public int Start { get; init; } = 1000;

    public int Floor { get; init; } = 100;

    public int K { get; init; } = 24;

    public int PlayerAdvantage { get; init; } = 50;
}

public sealed record StatsConfig : ModuleConfig
{
    public StatsConfig() => Version = 1;

    // Database.ServerKey also identifies this server in stats_servers.
    public DatabaseConfig Database { get; init; } = new();

    public int RawRetentionDays { get; init; } = 30;

    public int MaxShotsPerRound { get; init; } = 20000;

    public int MinPlayersPerSide { get; init; } = 1;

    public double TradeWindowSeconds { get; init; } = 5.0;

    public StatsRatingConfig Rating { get; init; } = new();

    public StatsEloConfig Elo { get; init; } = new();

    public int QueueCapacity { get; init; } = 200;

    public int ShutdownFlushSeconds { get; init; } = 5;

    // Shows each player's ELO in the scoreboard rank column (CS Rating style). Needs FollowCS2ServerGuidelines set to
    // false in CounterStrikeSharp's core.json, which goes against Valve's server guidelines: off by default.
    public bool ScoreboardElo { get; init; }

    public StatsRules ToRules() => new(
        TradeWindowSeconds,
        MinPlayersPerSide,
        MaxShotsPerRound,
        new RatingWeights(Rating.Base, Rating.Kill, Rating.Survive, Rating.Damage, Rating.Kast, Rating.Impact),
        new EloRules(Elo.Start, Elo.Floor, Elo.K, Elo.PlayerAdvantage));
}

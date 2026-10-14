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

    // Seconds between two stats_presence rows (humans, bots, queue); 0 = off.
    public int PresenceSnapshotSeconds { get; init; } = 60;

    // Seconds between two ping samples of each human (average and max per session); 0 = off.
    public int PingSampleSeconds { get; init; } = 30;

    // A server frame at least this long is recorded as a long_frame server event.
    public int LongFrameMs { get; init; } = 50;

    // DB-IP Lite country database (CC BY 4.0), relative to configs/plugins/RetakeV4. Missing file: country stays NULL.
    public string GeoDatabaseFile { get; init; } = "geo/dbip-country-lite.mmdb";

    // plugin_start, hot_reload, module_error, module_disabled, slow_handler, long_frame, uptime.
    public bool TrackServerHealth { get; init; } = true;

    // map_vote, map_vote_result, command (name only), chat (count only).
    public bool TrackUsage { get; init; } = true;

    // Movement sampling during the live phase, every this many server ticks (8 = 8 samples/s at 64 tick); 0 = off (no
    // movement or behaviour stats).
    public int SampleEveryTicks { get; init; } = 8;

    // Radius of a smoke cloud, in game units (72 u = about 1.8 m).
    public int SmokeRadius { get; init; } = 72;

    // A teammate killed closer than this to a living player (game units, 394 u = about 10 m) is a bait chance.
    public int BaitDistance { get; init; } = 394;

    // Seconds after a teammate's death during which the player can answer the killer (bait).
    public double BaitWindowSeconds { get; init; } = 5.0;

    // A period without a move of 2 m, a turn of 5 degrees or a shot counts as AFK from this long.
    public int AfkSeconds { get; init; } = 15;

    public StatsRules ToRules() => new(
        TradeWindowSeconds,
        MinPlayersPerSide,
        MaxShotsPerRound,
        new RatingWeights(Rating.Base, Rating.Kill, Rating.Survive, Rating.Damage, Rating.Kast, Rating.Impact),
        new EloRules(Elo.Start, Elo.Floor, Elo.K, Elo.PlayerAdvantage))
    {
        Movement = SampleEveryTicks > 0 ? new MovementRules(SmokeRadius, BaitDistance, BaitWindowSeconds, AfkSeconds) : null,
    };
}

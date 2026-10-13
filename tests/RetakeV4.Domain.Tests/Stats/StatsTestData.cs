using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

internal static class StatsTestData
{
    public const ulong Ct1 = 1;
    public const ulong Ct2 = 2;
    public const ulong Ct3 = 3;
    public const ulong T1 = 11;
    public const ulong T2 = 12;
    public const ulong T3 = 13;

    public static StatsRules Rules { get; } = new(
        TradeWindowSeconds: 5.0,
        MinPlayersPerSide: 1,
        MaxShotsPerRound: 1000,
        Rating: new RatingWeights(0.30, 0.35, 0.20, 0.004, 0.15, 0.20),
        Elo: new EloRules(Start: 1000, Floor: 100, K: 24, PlayerAdvantage: 50));

    public static RoundParticipant P(ulong id, TeamSide side, int elo = 1000) => new(id, $"p{id}", side, elo, null);

    public static RoundContext Context(params RoundParticipant[] participants) =>
        new("de_mirage", 1, BombSite.A, "FullBuy", new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero), participants);

    public static RoundRecord Record(RoundContext context, TeamSide? winner, Action<RoundRecorder>? facts = null, int minPlayersPerSide = 1)
    {
        var recorder = new RoundRecorder(1000, minPlayersPerSide);
        recorder.Open(context);
        facts?.Invoke(recorder);
        return recorder.Close(new RoundOutcome(winner, 7, 40000))!;
    }

    public static PlayerKilled Kill(ulong? attacker, ulong victim, int timeMs, string weapon = "ak47", bool headshot = false,
        ulong? assister = null, bool flashAssist = false, bool victimIsBot = false) =>
        new(attacker, victim, victimIsBot, assister, flashAssist, weapon, headshot, 0, false, false, false, false, 10f, null, null, timeMs);

    public static DamageDealt Hurt(ulong? attacker, ulong victim, int damage, int healthAfter, int timeMs, int tick,
        string weapon = "ak47", int hitgroup = 2, bool victimIsBot = false) =>
        new(attacker, victim, victimIsBot, weapon, hitgroup, damage, 0, healthAfter, timeMs, tick);

    public static PlayerRoundStats Of(RoundSummary summary, ulong id) => summary.Players.Single(p => p.SteamId == id);

    public static ShotFired Shot(ulong shooter, int timeMs, int tick, string weapon = "ak47") => new(shooter, weapon, timeMs, tick, null, null);
}

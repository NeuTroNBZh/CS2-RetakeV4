using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

internal static class StatsSamples
{
    public const ulong Ct = 76561198000000001UL;
    public const ulong T = 76561198000000002UL;

    public static StatsRules Rules { get; } = new(5.0, 1, 1000, new RatingWeights(0.30, 0.35, 0.20, 0.004, 0.15, 0.20), new EloRules(1000, 100, 24, 50));

    // CT kills T with a headshot on site A after one shot; T planted and threw a flash.
    public static RoundSummary Round(DateTimeOffset startedAt, string ctName = "ct", bool ranked = true, int ctElo = 1000, string weapon = "m4a1")
    {
        var participants = new[]
        {
            new RoundParticipant(Ct, ctName, TeamSide.CT, ctElo, weapon),
            new RoundParticipant(T, "t", TeamSide.T, 1000, "ak47"),
        };
        var recorder = new RoundRecorder(1000, ranked ? 1 : 2);
        recorder.Open(new RoundContext("de_mirage", 3, BombSite.A, "FullBuy", startedAt, participants));
        recorder.Add(new ShotFired(Ct, weapon, 100, 10, new Vec3(1, 2, 3), new ViewAngles(4, 5)));
        recorder.Add(new DamageDealt(Ct, T, false, weapon, 1, 100, 0, 0, 110, 10));
        recorder.Add(new PlayerKilled(Ct, T, false, null, false, weapon, true, 0, false, false, false, false, 12.5f, new Vec3(1, 2, 3), new Vec3(4, 5, 6), 110));
        recorder.Add(new GrenadeThrown(T, GrenadeType.Flash, 50, null));
        recorder.Add(new GrenadeDetonated(T, GrenadeType.Flash, 9, 90, new Vec3(7, 8, 9)));
        recorder.Add(new BombPlantedFact(T, 20));
        return RoundScoring.Process(recorder.Close(new RoundOutcome(TeamSide.CT, 7, 30000))!, Rules);
    }
}

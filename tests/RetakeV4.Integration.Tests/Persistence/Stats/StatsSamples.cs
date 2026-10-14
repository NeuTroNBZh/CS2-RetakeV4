using System.Globalization;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

internal static class StatsSamples
{
    public const ulong Ct = 76561198000000001UL;
    public const ulong T = 76561198000000002UL;
    public const ulong CtMate = 76561198000000003UL;
    public const ulong TMate = 76561198000000004UL;

    public static StatsRules Rules { get; } = new(5.0, 1, 1000, new RatingWeights(0.30, 0.35, 0.20, 0.004, 0.15, 0.20), new EloRules(1000, 100, 24, 50));

    public static StatsRules MovementOn { get; } = Rules with { Movement = new MovementRules(72f, 394f, 5.0, 15.0) };

    // Rows of the given players with at least one movement round column set, or one movement counter not 0 (any scope).
    public static string MovementRoundColumnsSet(string steamIds) =>
        $"SELECT COUNT(*) FROM stats_round_players WHERE steam_id IN ({steamIds}) AND ({string.Join(" OR ", RetakeV4.Persistence.Stats.StatsSchema.MovementRoundColumns.Select(c => $"{c} IS NOT NULL"))})";

    public static string MovementCountersSet(string steamIds) =>
        $"SELECT COUNT(*) FROM stats_player_totals WHERE steam_id IN ({steamIds}) AND ({string.Join(" OR ", RetakeV4.Persistence.Stats.StatsSchema.MovementCounters.Select(c => $"{c} <> 0"))})";

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

    // Site B. CT shoots once while crouched and slow (a still shot), kills T, reloads early and defuses with 0.6 s left;
    // T jumped before dying. CT ends with 87 health and 50 armor.
    public static RoundSummary RoundV2(DateTimeOffset startedAt)
    {
        var participants = new[]
        {
            new RoundParticipant(Ct, "ct", TeamSide.CT, 1000, "m4a1"),
            new RoundParticipant(T, "t", TeamSide.T, 1000, "ak47"),
        };
        var recorder = new RoundRecorder(1000, 1);
        recorder.Open(new RoundContext("de_mirage", 4, BombSite.B, "FullBuy", startedAt, participants));
        recorder.MarkFreezeEnd(0);
        recorder.Add(new ShotFired(Ct, "m4a1", 100, 10, new Vec3(1, 2, 3), new ViewAngles(4, 5)) { Motion = new ShotMotion(20f, true, false, false, false) });
        recorder.Add(new DamageDealt(Ct, T, false, "m4a1", 1, 100, 0, 0, 110, 10));
        recorder.Add(new PlayerKilled(Ct, T, false, null, false, "m4a1", true, 0, false, false, false, false, 12.5f, new Vec3(1, 2, 3), new Vec3(4, 5, 6), 110));
        recorder.Add(new PlayerEventFact(T, PlayerEventType.Jump, 50, null, new Vec3(4, 5, 6)));
        recorder.Add(PlayerEvents.Reload(Ct, "weapon_m4a1", 20, 30, 200, new Vec3(1, 2, 3)));
        recorder.Add(new BombPlantedFact(T, 20));
        recorder.Add(new BombDefusedFact(Ct, true, 5000) { RemainingMs = 600 });
        recorder.Add(new PlayerEndState(Ct, 87, 50));
        return RoundScoring.Process(recorder.Close(new RoundOutcome(TeamSide.CT, 7, 30000))!, Rules);
    }

    // Freeze ends at 1 s; three samples 0.5 s apart. ctA runs 250 u east and shoots at 1.5 s; ctB stands still and shoots
    // at 2 s; t stands still and never fights. Nobody dies; the round lasts 30 s. Scored with MovementOn unless rules are given.
    public static RoundSummary RoundMovement(DateTimeOffset startedAt, ulong ctA, ulong ctB, ulong t, StatsRules? rules = null)
    {
        var participants = new[]
        {
            new RoundParticipant(ctA, "cta", TeamSide.CT, 1000, "m4a1"),
            new RoundParticipant(ctB, "ctb", TeamSide.CT, 1000, "m4a1"),
            new RoundParticipant(t, "t", TeamSide.T, 1000, "ak47"),
        };
        var recorder = new RoundRecorder(1000, 1);
        recorder.Open(new RoundContext("de_mirage", 5, BombSite.A, "FullBuy", startedAt, participants));
        recorder.MarkFreezeEnd(1000);
        for (var i = 0; i < 3; i++)
        {
            var time = 1000 + (i * 500);
            recorder.Add(new MovementSample(ctA, time, new Vec3(i * 125, 0, 0), 250f, 0f, 0f, MovementFlags.None, 100));
            recorder.Add(new MovementSample(ctB, time, new Vec3(500, 0, 0), 0f, 0f, 0f, MovementFlags.None, 100));
            recorder.Add(new MovementSample(t, time, new Vec3(3000, 0, 0), 0f, 180f, 0f, MovementFlags.None, 100));
        }
        recorder.Add(new ShotFired(ctA, "m4a1", 1500, 96, null, null));
        recorder.Add(new ShotFired(ctB, "m4a1", 2000, 128, null, null));
        return RoundScoring.Process(recorder.Close(new RoundOutcome(TeamSide.CT, 9, 30000))!, rules ?? MovementOn);
    }

    // Site A, ranked. ct kills t with a no-scope wallbang (2 walls) at 100 ms; ctMate kills tMate through smoke, blind and
    // in the air at 200 ms. Every flag is also set on rows that are not kills: ct on a bot (50 ms), ctMate on t again
    // (duplicated death event, 150 ms), ct on ctMate (team kill, 250 ms), ct on himself (suicide, 300 ms).
    public static RoundSummary RoundSpecialKills(DateTimeOffset startedAt, ulong ct, ulong ctMate, ulong t, ulong tMate)
    {
        var participants = new[]
        {
            new RoundParticipant(ct, "ct", TeamSide.CT, 1000, "awp"),
            new RoundParticipant(ctMate, "ctmate", TeamSide.CT, 1000, "m4a1"),
            new RoundParticipant(t, "t", TeamSide.T, 1000, "ak47"),
            new RoundParticipant(tMate, "tmate", TeamSide.T, 1000, "ak47"),
        };
        var recorder = new RoundRecorder(1000, 1);
        recorder.Open(new RoundContext("de_mirage", 6, BombSite.A, "FullBuy", startedAt, participants));
        recorder.Add(SpecialKill(ct, 0, 50, 2, true, true, true, true, victimIsBot: true));
        recorder.Add(SpecialKill(ct, t, 100, 2, true, false, false, false));
        recorder.Add(SpecialKill(ctMate, t, 150, 2, true, true, true, true));
        recorder.Add(SpecialKill(ctMate, tMate, 200, 0, false, true, true, true));
        recorder.Add(SpecialKill(ct, ctMate, 250, 2, true, true, true, true));
        recorder.Add(SpecialKill(ct, ct, 300, 2, true, true, true, true));
        return RoundScoring.Process(recorder.Close(new RoundOutcome(TeamSide.CT, 7, 30000))!, Rules);
    }

    // After two RoundSpecialKills rounds: per-round columns, totals summed in every scope, shares of the kills. Values are
    // read through Convert so that SQLite and MySQL / MariaDB types both pass.
    public static async Task AssertSpecialKills(Func<string, Task<object?>> scalar, ulong ct, ulong ctMate, ulong t)
    {
        ArgumentNullException.ThrowIfNull(scalar);
        async Task<long[]> Counters(string from)
        {
            var values = new List<long>();
            foreach (var column in StatsCounters.SpecialKillColumns)
            {
                values.Add(Convert.ToInt64(await scalar($"SELECT {column} {from}"), CultureInfo.InvariantCulture));
            }
            return values.ToArray();
        }
        string FirstRound(ulong id) =>
            $"FROM v2_stats_round_players WHERE steam_id = {id} AND round_id = (SELECT MIN(round_id) FROM v2_stats_round_players WHERE steam_id = {id})";
        Assert.Equal(new long[] { 1, 1, 0, 0, 0 }, await Counters(FirstRound(ct)));
        Assert.Equal(new long[] { 0, 0, 1, 1, 1 }, await Counters(FirstRound(ctMate)));
        Assert.Equal(new long[5], await Counters(FirstRound(t)));
        foreach (var scope in new[] { "all", "map:de_mirage", "side:CT", "site:A", "roundtype:FullBuy" })
        {
            Assert.Equal(new long[] { 2, 2, 0, 0, 0 }, await Counters($"FROM v2_stats_player_scopes WHERE steam_id = {ct} AND scope = '{scope}'"));
            Assert.Equal(new long[] { 0, 0, 2, 2, 2 }, await Counters($"FROM v2_stats_player_scopes WHERE steam_id = {ctMate} AND scope = '{scope}'"));
        }
        Assert.Equal(new long[] { 2, 2, 0, 0, 0 }, await Counters($"FROM v2_stats_players WHERE steam_id = {ct}"));
        Assert.Equal(new long[5], await Counters($"FROM v2_stats_players WHERE steam_id = {t}"));
        Assert.Equal(2L, Convert.ToInt64(await scalar($"SELECT kills FROM v2_stats_players WHERE steam_id = {ct}"), CultureInfo.InvariantCulture));
        Assert.Equal(2L, Convert.ToInt64(await scalar($"SELECT team_kills FROM v2_stats_players WHERE steam_id = {ct}"), CultureInfo.InvariantCulture));
        Assert.Equal(100.0, Convert.ToDouble(await scalar($"SELECT noscope_kill_pct FROM v2_stats_players WHERE steam_id = {ct}"), CultureInfo.InvariantCulture));
        Assert.Equal(100.0, Convert.ToDouble(await scalar($"SELECT wallbang_kill_pct FROM v2_stats_players WHERE steam_id = {ct}"), CultureInfo.InvariantCulture));
        Assert.Equal(0.0, Convert.ToDouble(await scalar($"SELECT smoke_kill_pct FROM v2_stats_players WHERE steam_id = {ct}"), CultureInfo.InvariantCulture));
        Assert.Equal(100.0, Convert.ToDouble(await scalar($"SELECT air_kill_pct FROM v2_stats_player_scopes WHERE steam_id = {ctMate} AND scope = 'site:A'"), CultureInfo.InvariantCulture));
        Assert.Null(await scalar($"SELECT noscope_kill_pct FROM v2_stats_players WHERE steam_id = {t}"));
    }

    private static PlayerKilled SpecialKill(ulong? attacker, ulong victim, int timeMs, int penetrated, bool noScope, bool smoke, bool blind, bool air,
        bool victimIsBot = false) =>
        new(attacker, victim, victimIsBot, null, false, "awp", false, penetrated, noScope, smoke, blind, air, 10f, null, null, timeMs);
}

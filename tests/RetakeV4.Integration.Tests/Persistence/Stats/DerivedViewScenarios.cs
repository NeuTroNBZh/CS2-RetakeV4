using System.Globalization;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

// Seed-and-check scenarios of the derived views, run on SQLite (always) and on MySQL / MariaDB (RETAKEV4_MYSQL_TEST).
// The MySQL database is shared between runs: every scenario uses a fresh server key, fresh SteamIDs and fresh weapon
// names, and only reads its own rows. L and D read integers and doubles whatever the backend's CLR type.
internal static class DerivedViewScenarios
{
    internal static string NewKey(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    internal static ulong[] NewIds(int count) =>
        Enumerable.Range(0, count).Select(_ => 76561190000000000UL + (ulong)Random.Shared.NextInt64(1, 9_000_000_000)).ToArray();

    internal static long L(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);

    internal static double D(object? value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    // A timestamp column as a UTC instant: a string on SQLite, a DateTime on MySQL / MariaDB.
    private static DateTimeOffset At(object? value) => value switch
    {
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        string text => ViewSeed.Utc(text),
        _ => throw new InvalidOperationException($"Unexpected timestamp value: {value}"),
    };

    // One server: de_mirage A FullBuy ranked 3v2 CT win (defused, with kills), de_mirage A FullBuy ranked 3v2 T win
    // (exploded), de_mirage B Pistol unranked 2v2 CT win without a plant, de_nuke A FullBuy ranked 3v2 CT win (defused).
    // Returns the server key.
    internal static async Task<string> SeedRounds(ViewSeed seed)
    {
        var key = NewKey("dv");
        var ids = NewIds(5); // ct1, ct2, ct3, t1, t2
        var server = await seed.Server(key);
        var mirage = await seed.Map(server, "de_mirage", ViewSeed.Utc("2026-10-01 20:00:00"));
        var nuke = await seed.Map(server, "de_nuke", ViewSeed.Utc("2026-10-01 21:00:00"));
        var first = await seed.Round(mirage, new RoundSeed(ViewSeed.Utc("2026-10-01 20:01:00"),
            PlayersCt: 3, PlayersT: 2, Winner: "CT", PlantTimeMs: 10000, Defused: true, DurationMs: 40000));
        foreach (var (id, side, won) in new[] { (ids[0], "CT", true), (ids[1], "CT", true), (ids[2], "CT", true), (ids[3], "T", false), (ids[4], "T", false) })
        {
            await seed.RoundPlayer(first, id, side, won);
        }
        await seed.Kill(first, ids[0], 0, 3000, victimIsBot: true); // a bot: never a participant
        await seed.Kill(first, ids[0], ids[3], 5000);                // t1 dies before the plant
        await seed.Kill(first, ids[1], ids[3], 5000);                // duplicated death event of t1
        await seed.Kill(first, ids[4], ids[2], 10000);               // ct3 dies at the plant time: dead at the plant
        await seed.Kill(first, ids[4], ids[1], 12000);               // ct2 dies after the plant
        await seed.Round(mirage, new RoundSeed(ViewSeed.Utc("2026-10-01 20:03:00"),
            PlayersCt: 3, PlayersT: 2, Winner: "T", PlantTimeMs: 10000, Exploded: true, DurationMs: 50000));
        await seed.Round(mirage, new RoundSeed(ViewSeed.Utc("2026-10-01 20:05:00"),
            Site: "B", RoundType: "Pistol", PlayersCt: 2, PlayersT: 2, Ranked: false, Winner: "CT", DurationMs: 30000));
        await seed.Round(nuke, new RoundSeed(ViewSeed.Utc("2026-10-01 21:01:00"),
            PlayersCt: 3, PlayersT: 2, Winner: "CT", PlantTimeMs: 10000, Defused: true, DurationMs: 45000));
        return key;
    }

    internal static async Task SideBalance(ViewSeed seed)
    {
        var server = await SeedRounds(seed);
        var where = $"FROM v_stats_side_balance WHERE server = '{server}'";
        Assert.Equal(3L, L(await seed.Scalar($"SELECT COUNT(*) {where}")));
        var mirageA = $"{where} AND map_name = 'de_mirage' AND site = 'A'";
        Assert.IsType<long>(await seed.Scalar($"SELECT ct_wins {mirageA}")); // a BIGINT, never MySQL's DECIMAL
        Assert.Equal(2L, L(await seed.Scalar($"SELECT rounds {mirageA}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT ct_wins {mirageA}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT t_wins {mirageA}")));
        Assert.Equal(50.0, D(await seed.Scalar($"SELECT ct_win_pct {mirageA}")));
        Assert.Equal(50.0, D(await seed.Scalar($"SELECT t_win_pct {mirageA}")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT ranked_rounds {mirageA}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT ranked_ct_wins {mirageA}")));
        Assert.Equal(50.0, D(await seed.Scalar($"SELECT ranked_ct_win_pct {mirageA}")));
        Assert.Equal(45000.0, D(await seed.Scalar($"SELECT avg_duration_ms {mirageA}")));
        Assert.Equal(ViewSeed.Utc("2026-10-01 20:01:00"), At(await seed.Scalar($"SELECT first_round_at {mirageA}")));
        Assert.Equal(ViewSeed.Utc("2026-10-01 20:03:00"), At(await seed.Scalar($"SELECT last_round_at {mirageA}")));
        var mirageB = $"{where} AND map_name = 'de_mirage' AND site = 'B'";
        Assert.Equal(100.0, D(await seed.Scalar($"SELECT ct_win_pct {mirageB}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT ranked_rounds {mirageB}")));
        Assert.Null(await seed.Scalar($"SELECT ranked_ct_win_pct {mirageB}"));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT ct_wins {where} AND map_name = 'de_nuke' AND site = 'A'")));
    }
}

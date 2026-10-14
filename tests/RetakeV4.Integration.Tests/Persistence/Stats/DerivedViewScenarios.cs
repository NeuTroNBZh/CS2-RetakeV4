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

    // Situation at the plant: R1 had t1 (and its duplicated death) and ct3 (at the plant time) dead, a bot kill ignored.
    // A second 4v3 round proves that a team kill, a suicide and a world death are deaths like any other (the player is dead).
    internal static async Task RetakeOdds(ViewSeed seed)
    {
        var server = await SeedRounds(seed);
        var where = $"FROM v_stats_retake_odds WHERE server = '{server}'";
        Assert.Equal(4L, L(await seed.Scalar($"SELECT COUNT(*) {where}")));
        var won = $"{where} AND map_name = 'de_mirage' AND site = 'A' AND ct_alive_at_plant = 2 AND t_alive_at_plant = 1";
        Assert.Equal(1L, L(await seed.Scalar($"SELECT rounds {won}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT ct_wins {won}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT t_wins {won}")));
        Assert.Equal(100.0, D(await seed.Scalar($"SELECT ct_win_pct {won}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT defused_rounds {won}")));
        Assert.Equal(40000.0, D(await seed.Scalar($"SELECT avg_duration_ms {won}")));
        Assert.Equal("FullBuy", await seed.Scalar($"SELECT round_type {won}"));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT ranked {won}")));
        Assert.Equal(3L, L(await seed.Scalar($"SELECT players_ct {won}")));
        var lost = $"{where} AND map_name = 'de_mirage' AND site = 'A' AND ct_alive_at_plant = 3 AND t_alive_at_plant = 2";
        Assert.Equal(1L, L(await seed.Scalar($"SELECT t_wins {lost}")));
        Assert.Equal(0.0, D(await seed.Scalar($"SELECT ct_win_pct {lost}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT exploded_rounds {lost}")));
        var noPlant = $"{where} AND site = 'B'";
        Assert.Null(await seed.Scalar($"SELECT ct_alive_at_plant {noPlant}"));
        Assert.Null(await seed.Scalar($"SELECT t_alive_at_plant {noPlant}"));
        Assert.Equal("Pistol", await seed.Scalar($"SELECT round_type {noPlant}"));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT ranked {noPlant}")));
        Assert.Equal(3L, L(await seed.Scalar($"SELECT SUM(rounds) {where} AND players_ct = 3 AND players_t = 2")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT SUM(ct_wins) {where} AND players_ct = 3 AND players_t = 2")));

        // 4v3: ct1 kills its teammate ct2 (team kill), t1 kills itself, the world kills t2: all before the plant.
        var ids = NewIds(7); // ct1..ct4, t1..t3
        var otherKey = NewKey("dv");
        var inferno = await seed.Map(await seed.Server(otherKey), "de_inferno", ViewSeed.Utc("2026-10-01 22:00:00"));
        var round = await seed.Round(inferno, new RoundSeed(ViewSeed.Utc("2026-10-01 22:01:00"),
            PlayersCt: 4, PlayersT: 3, Winner: "CT", PlantTimeMs: 10000, Defused: true, DurationMs: 40000));
        for (var i = 0; i < 7; i++)
        {
            await seed.RoundPlayer(round, ids[i], i < 4 ? "CT" : "T", i < 4);
        }
        await seed.Kill(round, ids[0], ids[1], 2000, teamKill: true);
        await seed.Kill(round, ids[4], ids[4], 3000);
        await seed.Kill(round, null, ids[5], 4000);
        var teamDeaths = $"FROM v_stats_retake_odds WHERE server = '{otherKey}' AND map_name = 'de_inferno'";
        Assert.Equal(3L, L(await seed.Scalar($"SELECT ct_alive_at_plant {teamDeaths}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT t_alive_at_plant {teamDeaths}")));
    }

    // Rifle: fired by two players and primary of both in round 1; pistol: fired, never primary; sniper: primary in round 2,
    // never fired. kill_share_pct reads every weapon of the database, so it is only checked on a fresh one.
    internal static async Task WeaponMeta(ViewSeed seed, bool freshDatabase)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (rifle, pistol, sniper) = ($"wa_{tag}", $"wb_{tag}", $"wc_{tag}");
        var ids = NewIds(2);
        var server = await seed.Server(NewKey("dv"));
        var map = await seed.Map(server, "de_mirage", ViewSeed.Utc("2026-10-01 20:00:00"));
        var first = await seed.Round(map, new RoundSeed(ViewSeed.Utc("2026-10-01 20:01:00")));
        var second = await seed.Round(map, new RoundSeed(ViewSeed.Utc("2026-10-01 20:03:00")));
        await seed.PlayerWeapon(ids[0], rifle, 10, 5, 2, 2, 1, 300);
        await seed.PlayerWeapon(ids[1], rifle, 10, 3, 1, 2, 1, 200);
        await seed.PlayerWeapon(ids[0], pistol, 4, 1, 0, 0, 0, 50);
        await seed.RoundPlayer(first, ids[0], "CT", true, rifle);
        await seed.RoundPlayer(first, ids[1], "T", false, rifle);
        await seed.RoundPlayer(second, ids[0], "CT", true, sniper);
        string Of(string weapon) => $"FROM v_stats_weapon_meta WHERE weapon = '{weapon}'";
        Assert.Equal(2L, L(await seed.Scalar($"SELECT players {Of(rifle)}")));
        Assert.Equal(20L, L(await seed.Scalar($"SELECT shots {Of(rifle)}")));
        Assert.Equal(8L, L(await seed.Scalar($"SELECT hits {Of(rifle)}")));
        Assert.Equal(3L, L(await seed.Scalar($"SELECT headshot_hits {Of(rifle)}")));
        Assert.Equal(4L, L(await seed.Scalar($"SELECT kills {Of(rifle)}")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT headshot_kills {Of(rifle)}")));
        Assert.Equal(500L, L(await seed.Scalar($"SELECT damage {Of(rifle)}")));
        Assert.Equal(40.0, D(await seed.Scalar($"SELECT accuracy {Of(rifle)}")));
        Assert.Equal(37.5, D(await seed.Scalar($"SELECT hs_accuracy {Of(rifle)}")));
        Assert.Equal(50.0, D(await seed.Scalar($"SELECT hs_pct {Of(rifle)}")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT primary_rounds {Of(rifle)}")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT primary_players {Of(rifle)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT primary_won_rounds {Of(rifle)}")));
        Assert.Equal(50.0, D(await seed.Scalar($"SELECT primary_win_pct {Of(rifle)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT players {Of(pistol)}")));
        Assert.Equal(25.0, D(await seed.Scalar($"SELECT accuracy {Of(pistol)}")));
        Assert.Null(await seed.Scalar($"SELECT hs_pct {Of(pistol)}"));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT primary_rounds {Of(pistol)}")));
        Assert.Null(await seed.Scalar($"SELECT primary_win_pct {Of(pistol)}"));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT players {Of(sniper)}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT shots {Of(sniper)}")));
        Assert.Null(await seed.Scalar($"SELECT accuracy {Of(sniper)}"));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT primary_rounds {Of(sniper)}")));
        Assert.Equal(100.0, D(await seed.Scalar($"SELECT primary_win_pct {Of(sniper)}")));
        if (freshDatabase)
        {
            Assert.Equal(3L, L(await seed.Scalar("SELECT COUNT(*) FROM v_stats_weapon_meta")));
            Assert.Equal(100.0, D(await seed.Scalar($"SELECT kill_share_pct {Of(rifle)}")));
            Assert.Equal(0.0, D(await seed.Scalar($"SELECT kill_share_pct {Of(pistol)}")));
        }
    }

    // Players a, b, c, d, e. Round 1: a, b (CT) lose to c, d (T); c kills a (headshot), a duplicated death of a by d, b kills
    // a bot, b kills c exactly 5 s after a died (a trade), d kills b 3 s after c died (a trade). Round 2: a, e (CT) beat
    // c, d (T); d team-kills c, a kills d (headshot), the world kills e. Round 3: a, b (CT) lose to c, d (T); c kills a,
    // b kills c 5.001 s later (too late for a trade). Returns { a, b, c, d, e }.
    internal static async Task<ulong[]> SeedDuels(ViewSeed seed)
    {
        var ids = NewIds(5);
        var (a, b, c, d, e) = (ids[0], ids[1], ids[2], ids[3], ids[4]);
        foreach (var id in ids)
        {
            await seed.Player(id);
        }
        var server = await seed.Server(NewKey("dv"));
        var map = await seed.Map(server, "de_mirage", ViewSeed.Utc("2026-10-01 20:00:00"));
        var first = await seed.Round(map, new RoundSeed(ViewSeed.Utc("2026-10-01 20:01:00"), Winner: "T"));
        await RoundPlayers(seed, first, (a, "CT", false), (b, "CT", false), (c, "T", true), (d, "T", true));
        await seed.Kill(first, c, a, 1000, headshot: true);
        await seed.Kill(first, d, a, 1001);                    // duplicated death of a: only the first row counts
        await seed.Kill(first, b, 0, 2000, victimIsBot: true); // a bot victim: never a duel
        await seed.Kill(first, b, c, 6000);                    // 5 s after a's death: b trades a
        await seed.Kill(first, d, b, 9000);                    // 3 s after c's death: d trades c
        var second = await seed.Round(map, new RoundSeed(ViewSeed.Utc("2026-10-01 20:03:00"), Winner: "CT"));
        await RoundPlayers(seed, second, (a, "CT", true), (e, "CT", true), (c, "T", false), (d, "T", false));
        await seed.Kill(second, d, c, 500, teamKill: true);    // team kill: not a duel
        await seed.Kill(second, a, d, 2000, headshot: true);
        await seed.Kill(second, null, e, 3000);                // the world: no attacker
        var third = await seed.Round(map, new RoundSeed(ViewSeed.Utc("2026-10-01 20:05:00"), Winner: "T"));
        await RoundPlayers(seed, third, (a, "CT", false), (b, "CT", false), (c, "T", true), (d, "T", true));
        await seed.Kill(third, c, a, 1000);
        await seed.Kill(third, b, c, 6001);                    // 5.001 s: too late for a trade
        return ids;
    }

    private static async Task RoundPlayers(ViewSeed seed, long roundId, params (ulong Id, string Side, bool Won)[] players)
    {
        foreach (var (id, side, won) in players)
        {
            await seed.RoundPlayer(roundId, id, side, won);
        }
    }

    internal static async Task Rivals(ViewSeed seed)
    {
        var ids = await SeedDuels(seed);
        var (a, b, c, d, e) = (ids[0], ids[1], ids[2], ids[3], ids[4]);
        string Pair(ulong player, ulong opponent) => $"FROM v_stats_rivals WHERE steam_id = {player} AND opponent_id = {opponent}";
        Assert.Equal(2L, L(await seed.Scalar($"SELECT kills {Pair(c, a)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT headshot_kills {Pair(c, a)}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT deaths {Pair(c, a)}")));
        Assert.Null(await seed.Scalar($"SELECT kd {Pair(c, a)}"));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT kills {Pair(a, c)}")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT deaths {Pair(a, c)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT headshot_deaths {Pair(a, c)}")));
        Assert.Equal(0.0, D(await seed.Scalar($"SELECT kd {Pair(a, c)}")));
        Assert.Equal($"p{c}", await seed.Scalar($"SELECT opponent_name {Pair(a, c)}"));
        Assert.Equal($"p{a}", await seed.Scalar($"SELECT name {Pair(a, c)}"));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT kills {Pair(b, c)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT kills {Pair(d, b)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT headshot_kills {Pair(a, d)}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT kills {Pair(d, a)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT deaths {Pair(d, a)}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT COUNT(*) {Pair(d, c)}")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT COUNT(*) FROM v_stats_rivals WHERE steam_id = {d}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT COUNT(*) FROM v_stats_rivals WHERE steam_id = {e} OR opponent_id = {e}")));
    }

    internal static async Task Duos(ViewSeed seed)
    {
        var ids = await SeedDuels(seed);
        var (a, b, c, d, e) = (ids[0], ids[1], ids[2], ids[3], ids[4]);
        string Pair(ulong player, ulong mate) => $"FROM v_stats_duos WHERE steam_id = {player} AND mate_id = {mate}";
        Assert.Equal(2L, L(await seed.Scalar($"SELECT rounds {Pair(a, b)}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT rounds_won {Pair(a, b)}")));
        Assert.Equal(0.0, D(await seed.Scalar($"SELECT win_pct {Pair(a, b)}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT trades {Pair(a, b)}")));
        Assert.Equal($"p{b}", await seed.Scalar($"SELECT mate_name {Pair(a, b)}"));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT trades {Pair(b, a)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT rounds {Pair(a, e)}")));
        Assert.Equal(100.0, D(await seed.Scalar($"SELECT win_pct {Pair(a, e)}")));
        Assert.Equal(3L, L(await seed.Scalar($"SELECT rounds {Pair(c, d)}")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT rounds_won {Pair(c, d)}")));
        Assert.Equal(66.667, D(await seed.Scalar($"SELECT win_pct {Pair(c, d)}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT trades {Pair(c, d)}")));
        Assert.Equal(1L, L(await seed.Scalar($"SELECT trades {Pair(d, c)}")));
        Assert.Equal(2L, L(await seed.Scalar($"SELECT COUNT(*) FROM v_stats_duos WHERE steam_id = {a}")));
        Assert.Equal(0L, L(await seed.Scalar($"SELECT COUNT(*) {Pair(a, c)}")));
    }
}

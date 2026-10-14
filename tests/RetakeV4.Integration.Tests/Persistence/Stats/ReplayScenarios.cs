using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using RetakeV4.Persistence.Stats;
using static RetakeV4.Integration.Tests.Persistence.Stats.DerivedViewScenarios;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

// Replay selection in the database, run on SQLite (always) and on MySQL / MariaDB (RETAKEV4_MYSQL_TEST). The MySQL
// database is shared between runs: every scenario uses fresh SteamIDs (and the callers fresh server keys) and only reads
// its own rows. scalar runs one query and returns null for DBNull.
internal static class ReplayScenarios
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);

    internal sealed record Written(long RoundId, ReplayRound Replay);

    // One RoundMovement round (ids[0] "cta" and ids[1] "ctb" CT, ids[2] "t" T: three tracks of three frames), written as
    // the module writes it: replay encoded first, then the summary without its samples.
    internal static async Task<Written> WriteRound(SqlStatsRepository repository, long mapId, DateTimeOffset at, ulong[] ids)
    {
        var summary = StatsSamples.RoundMovement(at, ids[0], ids[1], ids[2]);
        var replay = ReplayEncoder.Encode(summary.Record, 72f, 8)!;
        var roundId = await repository.WriteRoundAsync(mapId, summary.WithoutSamples(), CancellationToken.None);
        return new Written(roundId, replay);
    }

    internal static Task<int> Claim(SqlStatsRepository repository, Written round, ulong steamId, ReplayReason reason, double score, ReplayCaps caps, DateTimeOffset at) =>
        repository.WriteReplayAsync(round.RoundId, round.Replay, new[] { new ReplayClaim(steamId, reason, score) }, caps, at, CancellationToken.None);

    private static async Task<long> Count(Func<string, Task<object?>> scalar, string sql) => L(await scalar(sql));

    private static async Task<List<Written>> Rounds(SqlStatsRepository repository, int count, ulong[] ids)
    {
        var map = await repository.OpenMapAsync("de_mirage", T0, CancellationToken.None);
        var rounds = new List<Written>();
        for (var i = 0; i < count; i++)
        {
            rounds.Add(await WriteRound(repository, map, T0.AddMinutes(i), ids));
        }
        return rounds;
    }

    private static async Task AssertGone(Func<string, Task<object?>> scalar, long roundId)
    {
        Assert.Equal(0, await Count(scalar, $"SELECT COUNT(*) FROM stats_replays WHERE round_id = {roundId}"));
        Assert.Equal(0, await Count(scalar, $"SELECT COUNT(*) FROM stats_replay_tracks WHERE round_id = {roundId}"));
        Assert.Equal(0, await Count(scalar, $"SELECT COUNT(*) FROM stats_replay_owners WHERE round_id = {roundId}"));
    }

    // best, cap 2: 1.0, 2.0 then 1.5 evicts 1.0 (its round has no owner left: deleted); a fourth at 1.5 (not strictly
    // better than the weakest) is refused and leaves no row. The kept rounds show every column of both views.
    internal static async Task BestKeepsTheStrongest(SqlStatsRepository repository, string server, Func<string, Task<object?>> scalar)
    {
        var ids = NewIds(3);
        var caps = new ReplayCaps(2, 10, 20);
        var rounds = await Rounds(repository, 4, ids);
        Assert.Equal(1, await Claim(repository, rounds[0], ids[0], ReplayReason.Best, 1.0, caps, T0));
        Assert.Equal(1, await Claim(repository, rounds[1], ids[0], ReplayReason.Best, 2.0, caps, T0.AddMinutes(1)));
        Assert.Equal(1, await Claim(repository, rounds[2], ids[0], ReplayReason.Best, 1.5, caps, T0.AddMinutes(2)));
        Assert.Equal(0, await Claim(repository, rounds[3], ids[0], ReplayReason.Best, 1.5, caps, T0.AddMinutes(3)));

        Assert.Equal(2, await Count(scalar, $"SELECT COUNT(*) FROM v_stats_replays WHERE steam_id = {ids[0]} AND reason = 'best'"));
        await AssertGone(scalar, rounds[0].RoundId);
        await AssertGone(scalar, rounds[3].RoundId);
        Assert.Equal(2.0, D(await scalar($"SELECT MAX(score) FROM v_stats_replays WHERE steam_id = {ids[0]}")));
        Assert.Equal(1.5, D(await scalar($"SELECT MIN(score) FROM v_stats_replays WHERE steam_id = {ids[0]}")));

        var kept = rounds[1];
        var row = $"FROM v_stats_replays WHERE round_id = {kept.RoundId} AND steam_id = {ids[0]}";
        Assert.Equal(server, await scalar($"SELECT server {row}"));
        Assert.Equal("de_mirage", await scalar($"SELECT map_name {row}"));
        Assert.Equal(5L, L(await scalar($"SELECT round_number {row}")));
        Assert.Equal("A", await scalar($"SELECT site {row}"));
        Assert.Equal("FullBuy", await scalar($"SELECT round_type {row}"));
        Assert.Equal("CT", await scalar($"SELECT winner {row}"));
        Assert.Equal(30000L, L(await scalar($"SELECT duration_ms {row}")));
        Assert.Equal(8.0, D(await scalar($"SELECT sample_hz {row}")));
        Assert.Equal((long)kept.Replay.Bytes, L(await scalar($"SELECT bytes {row}")));
        Assert.Equal("cta", await scalar($"SELECT name {row}"));
        Assert.Equal("best", await scalar($"SELECT reason {row}"));
        Assert.NotNull(await scalar($"SELECT owned_at {row}"));
        Assert.NotNull(await scalar($"SELECT created_at {row}"));
        Assert.NotNull(await scalar($"SELECT started_at {row}"));
        Assert.Equal(3, await Count(scalar, $"SELECT COUNT(*) FROM v_stats_replay_tracks WHERE round_id = {kept.RoundId}"));
        var track = $"FROM v_stats_replay_tracks WHERE round_id = {kept.RoundId} AND steam_id = {ids[2]}";
        Assert.Equal(kept.Replay.Tracks[2].Frames, await scalar($"SELECT frames {track}"));
        Assert.Equal("T", await scalar($"SELECT side {track}"));
        Assert.Equal("t", await scalar($"SELECT name {track}"));
    }

    // highlight, cap 2: the third evicts the oldest whatever the score; saved, cap 1: a saved round comes back (rows
    // written again), then a newer save evicts it (deleted, no owner left) while the round that is also a highlight stays.
    // Saving the same round twice changes nothing.
    internal static async Task HighlightsAndSavedReplaceTheOldest(SqlStatsRepository repository, Func<string, Task<object?>> scalar)
    {
        var ids = NewIds(3);
        var caps = new ReplayCaps(10, 2, 1);
        var rounds = await Rounds(repository, 3, ids);
        await Claim(repository, rounds[0], ids[0], ReplayReason.Highlight, 9.0, caps, T0);
        await Claim(repository, rounds[1], ids[0], ReplayReason.Highlight, 0.1, caps, T0.AddMinutes(1));
        await Claim(repository, rounds[2], ids[0], ReplayReason.Highlight, 0.1, caps, T0.AddMinutes(2));
        await AssertGone(scalar, rounds[0].RoundId);

        Assert.Equal(1, await Claim(repository, rounds[0], ids[0], ReplayReason.Saved, 9.0, caps, T0.AddMinutes(3)));
        Assert.Equal(3, await Count(scalar, $"SELECT COUNT(*) FROM stats_replay_tracks WHERE round_id = {rounds[0].RoundId}"));

        Assert.Equal(2, await Claim(repository, rounds[1], ids[0], ReplayReason.Saved, 0.1, caps, T0.AddMinutes(4)));
        await AssertGone(scalar, rounds[0].RoundId);
        Assert.Equal(2, await Claim(repository, rounds[1], ids[0], ReplayReason.Saved, 0.1, caps, T0.AddMinutes(5)));

        Assert.Equal(2, await Count(scalar, $"SELECT COUNT(*) FROM stats_replay_owners WHERE steam_id = {ids[0]} AND reason = 'highlight'"));
        Assert.Equal(1, await Count(scalar, $"SELECT COUNT(*) FROM stats_replay_owners WHERE steam_id = {ids[0]} AND reason = 'saved' AND round_id = {rounds[1].RoundId}"));
        Assert.Equal(1, await Count(scalar, $"SELECT COUNT(*) FROM stats_replays WHERE round_id = {rounds[1].RoundId}"));
    }

    // ids[0] owns r0 as best, ids[1] as saved: when ids[0]'s better round evicts r0, r0 stays for ids[1].
    internal static async Task SharedReplayStaysWhileOwned(SqlStatsRepository repository, Func<string, Task<object?>> scalar)
    {
        var ids = NewIds(3);
        var caps = new ReplayCaps(1, 10, 20);
        var rounds = await Rounds(repository, 2, ids);
        await Claim(repository, rounds[0], ids[0], ReplayReason.Best, 1.0, caps, T0);
        Assert.Equal(2, await Claim(repository, rounds[0], ids[1], ReplayReason.Saved, 1.0, caps, T0.AddMinutes(1)));
        await Claim(repository, rounds[1], ids[0], ReplayReason.Best, 2.0, caps, T0.AddMinutes(2));

        Assert.Equal(1, await Count(scalar, $"SELECT COUNT(*) FROM stats_replays WHERE round_id = {rounds[0].RoundId}"));
        Assert.Equal(3, await Count(scalar, $"SELECT COUNT(*) FROM stats_replay_tracks WHERE round_id = {rounds[0].RoundId}"));
        Assert.Equal(0, await Count(scalar, $"SELECT COUNT(*) FROM stats_replay_owners WHERE round_id = {rounds[0].RoundId} AND steam_id = {ids[0]}"));
    }

    // Two servers on one database, best cap 3 for one player: six rounds (three per server, scores 1, 5, 3 and 4, 2, 6)
    // claimed one server after the other, or both at once (concurrent: MySQL / MariaDB). Whatever the interleaving, the
    // player keeps exactly the three best rounds and no replay is left without an owner.
    internal static async Task TwoServersKeepTheCap(SqlStatsRepository first, SqlStatsRepository second, Func<string, Task<object?>> scalar, bool concurrent)
    {
        var ids = NewIds(3);
        var caps = new ReplayCaps(3, 10, 20);
        var a = await Rounds(first, 3, ids);
        var b = await Rounds(second, 3, ids);
        async Task ClaimAll(SqlStatsRepository repository, List<Written> rounds, double[] scores)
        {
            for (var i = 0; i < rounds.Count; i++)
            {
                await Claim(repository, rounds[i], ids[0], ReplayReason.Best, scores[i], caps, T0.AddMinutes(i));
            }
        }
        if (concurrent)
        {
            await Task.WhenAll(Task.Run(() => ClaimAll(first, a, new[] { 1.0, 5.0, 3.0 })), Task.Run(() => ClaimAll(second, b, new[] { 4.0, 2.0, 6.0 })));
        }
        else
        {
            await ClaimAll(first, a, new[] { 1.0, 5.0, 3.0 });
            await ClaimAll(second, b, new[] { 4.0, 2.0, 6.0 });
        }

        var all = string.Join(", ", a.Concat(b).Select(r => r.RoundId));
        Assert.Equal(3, await Count(scalar, $"SELECT COUNT(*) FROM stats_replay_owners WHERE steam_id = {ids[0]} AND reason = 'best'"));
        Assert.Equal(4.0, D(await scalar($"SELECT MIN(score) FROM stats_replay_owners WHERE steam_id = {ids[0]} AND reason = 'best'")));
        Assert.Equal(6.0, D(await scalar($"SELECT MAX(score) FROM stats_replay_owners WHERE steam_id = {ids[0]} AND reason = 'best'")));
        Assert.Equal(3, await Count(scalar, $"SELECT COUNT(*) FROM stats_replays WHERE round_id IN ({all})"));
        Assert.Equal(0, await Count(scalar,
            $"SELECT COUNT(*) FROM stats_replays r WHERE r.round_id IN ({all}) AND NOT EXISTS (SELECT 1 FROM stats_replay_owners o WHERE o.round_id = r.round_id)"));
    }

    // A track above the 64 KB of a MySQL TEXT (about 105 KB) is stored and read back whole.
    internal static async Task LongFramesRoundTrip(SqlStatsRepository repository, Func<string, Task<object?>> scalar)
    {
        var ids = NewIds(3);
        var round = (await Rounds(repository, 1, ids))[0];
        var frames = "[" + string.Join(",", Enumerable.Repeat("[123456,-1234,2345,-123,270,17,100]", 3000)) + "]";
        var replay = new ReplayRound(8, new[] { new ReplayTrack(ids[0], TeamSide.CT, frames, 3000) });
        var written = new Written(round.RoundId, replay);
        Assert.Equal(1, await Claim(repository, written, ids[0], ReplayReason.Saved, 1.0, new ReplayCaps(10, 10, 20), T0));
        Assert.Equal((long)frames.Length, L(await scalar($"SELECT LENGTH(frames) FROM v_stats_replay_tracks WHERE round_id = {round.RoundId}")));
        Assert.Equal((long)replay.Bytes, L(await scalar($"SELECT bytes FROM v_stats_replays WHERE round_id = {round.RoundId}")));
    }

    // ids[0] and ids[1] both own r0 as best (cap 1); then, at once from two repositories (two connections), ids[0] keeps
    // r1 and ids[1] keeps r2, each evicting r0. Whatever the interleaving, both writes succeed and r0, left without an
    // owner, is deleted. Repeated on fresh players and rounds to vary the interleaving.
    internal static async Task SharedRoundEvictedConcurrently(SqlStatsRepository first, SqlStatsRepository second, Func<string, Task<object?>> scalar)
    {
        var caps = new ReplayCaps(1, 10, 20);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var ids = NewIds(3);
            var rounds = await Rounds(first, 3, ids);
            Assert.Equal(1, await Claim(first, rounds[0], ids[0], ReplayReason.Best, 1.0, caps, T0));
            Assert.Equal(2, await Claim(first, rounds[0], ids[1], ReplayReason.Best, 1.0, caps, T0));

            var owners = await Task.WhenAll(
                Task.Run(() => Claim(first, rounds[1], ids[0], ReplayReason.Best, 2.0, caps, T0.AddMinutes(1))),
                Task.Run(() => Claim(second, rounds[2], ids[1], ReplayReason.Best, 2.0, caps, T0.AddMinutes(1))));

            Assert.Equal(new[] { 1, 1 }, owners);
            await AssertGone(scalar, rounds[0].RoundId);
            Assert.Equal(1, await Count(scalar, $"SELECT COUNT(*) FROM stats_replays WHERE round_id = {rounds[1].RoundId}"));
            Assert.Equal(1, await Count(scalar, $"SELECT COUNT(*) FROM stats_replays WHERE round_id = {rounds[2].RoundId}"));
            Assert.Equal(1, await Count(scalar, $"SELECT COUNT(*) FROM v_stats_replays WHERE steam_id = {ids[0]} AND round_id = {rounds[1].RoundId}"));
            Assert.Equal(1, await Count(scalar, $"SELECT COUNT(*) FROM v_stats_replays WHERE steam_id = {ids[1]} AND round_id = {rounds[2].RoundId}"));
        }
    }

    // A claim for a SteamID that never played (no stats_players row) and an empty claim list store nothing.
    internal static async Task UnknownPlayersAndNoClaims_StoreNothing(SqlStatsRepository repository, Func<string, Task<object?>> scalar)
    {
        var ids = NewIds(4);
        var round = (await Rounds(repository, 1, ids))[0];
        Assert.Equal(0, await Claim(repository, round, ids[3], ReplayReason.Saved, 1.0, new ReplayCaps(10, 10, 20), T0));
        Assert.Equal(0, await repository.WriteReplayAsync(round.RoundId, round.Replay, Array.Empty<ReplayClaim>(), new ReplayCaps(10, 10, 20), T0, CancellationToken.None));
        await AssertGone(scalar, round.RoundId);
    }
}

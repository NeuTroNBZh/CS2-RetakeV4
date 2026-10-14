using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Persistence.Stats;

// One repository for both backends: table and column names are constants, every value is a parameter.
public sealed class SqlStatsRepository : IStatsRepository
{
    private const int BatchRows = 500;

    // Column sizes of StatsSchema: MySQL strict mode rejects longer values, which would fail the whole round.
    private const int PlayerNameLength = 128;
    private const int NameLength = 64;
    private const int ScopeLength = 80;
    private const int GrenadeTypeLength = 16;
    private const int EventTypeLength = 24;
    private const int EventValueLength = 32;
    private const int CountryLength = 2;
    private const int ServerEventValueLength = 128;
    private const int ReasonLength = 16;

    private static readonly string[] RoundColumns =
    {
        "map_id", "round_number", "site", "round_type", "players_ct", "players_t", "ranked", "winner", "end_reason", "duration_ms",
        "planted", "plant_site", "planter", "plant_time_ms", "defused", "defuser", "defused_with_kit", "bomb_exploded", "started_at",
    };

    private static readonly string[] RoundPlayerColumns =
    {
        "round_id", "steam_id", "side", "primary_weapon", "survived", "won", "kills", "deaths", "assists", "flash_assists", "team_kills",
        "damage", "utility_damage", "headshot_kills", "shots", "hits", "headshot_hits", "entry_kill", "entry_death", "trade_kills",
        "traded_death", "kast", "clutch_vs", "clutch_won", "enemies_flashed", "teammates_flashed", "blind_seconds_inflicted", "mvp",
        "planted", "defused", "defused_with_kit", "rating", "elo_before", "elo_after",
        "shots_still", "hits_still", "shots_moving", "hits_moving", "shots_scoped", "hits_scoped", "damage_taken", "bomb_carry_ms",
        "health_end", "armor_end",
        "alive_ms", "walk_pct", "run_pct", "crouch_pct", "still_pct", "air_pct", "distance_m", "afk_ms", "in_smoke_pct",
        "behind_smoke_pct", "first_contact_ms", "engage_order", "bait",
        "noscope_kills", "wallbang_kills", "smoke_kills", "blind_kills", "air_kills",
    };

    private static readonly string[] ShotColumns =
    {
        "round_id", "steam_id", "weapon", "time_ms", "tick", "pos_x", "pos_y", "pos_z", "pitch", "yaw",
        "speed", "crouched", "scoped", "airborne", "walking", "still",
    };

    private static readonly string[] EventColumns = { "round_id", "steam_id", "type", "time_ms", "value", "pos_x", "pos_y", "pos_z" };

    private static readonly string[] DamageColumns =
    {
        "round_id", "attacker", "victim", "victim_is_bot", "weapon", "hitgroup", "dmg_health", "dmg_armor", "health_after", "time_ms", "tick",
    };

    private static readonly string[] KillColumns =
    {
        "round_id", "attacker", "victim", "victim_is_bot", "assister", "flash_assist", "weapon", "headshot", "penetrated", "noscope",
        "thrusmoke", "attacker_blind", "attacker_in_air", "distance", "attacker_x", "attacker_y", "attacker_z", "victim_x", "victim_y",
        "victim_z", "time_ms", "is_team_kill",
    };

    private static readonly string[] GrenadeColumns =
    {
        "round_id", "steam_id", "type", "thrown_ms", "detonated_ms", "throw_x", "throw_y", "throw_z", "det_x", "det_y", "det_z",
        "damage", "enemies_blinded", "teammates_blinded", "blind_seconds",
    };

    private static readonly string[] TrackColumns = { "round_id", "steam_id", "side", "frames" };

    private readonly StatsDialect _dialect;
    private readonly Func<CancellationToken, Task<DbConnection>> _open;
    private readonly StatsWriteOptions _options;

    // stats_servers id of ServerKey, cached once a transaction that resolved it has committed (the row is never deleted).
    private long? _serverId;

    public SqlStatsRepository(StatsDialect dialect, Func<CancellationToken, Task<DbConnection>> open, StatsWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(options);
        _dialect = dialect;
        _open = open;
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await _open(ct).ConfigureAwait(false);
        await SqlMigrator.ApplyAsync(connection, StatsSchema.Migrations(_dialect), ct, StatsSchema.VersionTable).ConfigureAwait(false);
    }

    public async Task<long> OpenMapAsync(string mapName, DateTimeOffset startedAt, CancellationToken ct)
    {
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var at = _dialect.DateParam(startedAt);
        var serverId = await EnsureServerAsync(connection, transaction, at, touch: true, ct).ConfigureAwait(false);
        var mapId = await InsertAsync(connection, transaction, "stats_maps", new[] { "server_id", "map_name", "started_at" },
            new object?[] { serverId, Clip(mapName, NameLength), at }, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _serverId ??= serverId;
        return mapId;
    }

    // touch (map opening): upsert the row, last_seen only moving forward. Otherwise (telemetry) the cached id is reused
    // and the row is only written when it was never resolved by this repository.
    private async Task<long> EnsureServerAsync(DbConnection connection, DbTransaction transaction, object at, bool touch, CancellationToken ct)
    {
        if (_serverId is { } cached && !touch)
        {
            return cached;
        }
        await ExecuteAsync(connection, transaction,
            $"INSERT INTO stats_servers (name, first_seen, last_seen) VALUES (@name, @at, @at) {_dialect.OnConflictKeepLatest(new[] { "name" }, "last_seen", "@at")}",
            ct, ("@name", _options.ServerKey), ("@at", at)).ConfigureAwait(false);
        return _serverId ?? Convert.ToInt64(
            await ScalarAsync(connection, transaction, "SELECT id FROM stats_servers WHERE name = @name", ct, ("@name", _options.ServerKey)).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    public async Task CloseMapAsync(long mapId, DateTimeOffset endedAt, CancellationToken ct)
    {
        await using var connection = await _open(ct).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "UPDATE stats_maps SET ended_at = @at WHERE id = @id", ct,
            ("@at", _dialect.DateParam(endedAt)), ("@id", mapId)).ConfigureAwait(false);
    }

    public async Task<long> OpenSessionAsync(long mapId, ulong steamId, string name, DateTimeOffset connectedAt, CancellationToken ct)
    {
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await UpsertPlayerAsync(connection, transaction, steamId, name, connectedAt, ct).ConfigureAwait(false);
        var sessionId = await InsertAsync(connection, transaction, "stats_sessions", new[] { "steam_id", "map_id", "connected_at" },
            new[] { _dialect.SteamParam(steamId), mapId, _dialect.DateParam(connectedAt) }, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return sessionId;
    }

    public async Task CloseSessionAsync(long? sessionId, long mapId, ulong steamId, DateTimeOffset disconnectedAt, int? reason, Playtime playtime,
        SessionActivity activity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(playtime);
        ArgumentNullException.ThrowIfNull(activity);
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var at = _dialect.DateParam(disconnectedAt);
        if (sessionId is { } id)
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE stats_sessions SET disconnected_at = @at, disconnect_reason = @reason,
                    seconds_ct = @ct, seconds_t = @t, seconds_spec = @spec, seconds_queue = @queue,
                    seconds_vip = @vip, ping_avg = @ping_avg, ping_max = @ping_max
                WHERE id = @id
                """, ct, ("@at", at), ("@reason", reason), ("@ct", playtime.SecondsCt), ("@t", playtime.SecondsT),
                ("@spec", playtime.SecondsSpectator), ("@queue", playtime.SecondsQueue), ("@vip", playtime.SecondsVip),
                ("@ping_avg", activity.PingAvg), ("@ping_max", activity.PingMax), ("@id", id)).ConfigureAwait(false);
        }
        else
        {
            await InsertAsync(connection, transaction, "stats_sessions",
                new[]
                {
                    "steam_id", "map_id", "disconnected_at", "disconnect_reason", "seconds_ct", "seconds_t", "seconds_spec", "seconds_queue",
                    "seconds_vip", "ping_avg", "ping_max",
                },
                new object?[]
                {
                    _dialect.SteamParam(steamId), mapId, at, reason,
                    playtime.SecondsCt, playtime.SecondsT, playtime.SecondsSpectator, playtime.SecondsQueue,
                    playtime.SecondsVip, activity.PingAvg, activity.PingMax,
                }, ct).ConfigureAwait(false);
        }
        var columns = new[] { "seconds_ct", "seconds_t", "seconds_spec", "seconds_queue", "sessions" };
        await ExecuteAsync(connection, transaction,
            $"INSERT INTO stats_player_playtime (steam_id, {string.Join(", ", columns)}) VALUES (@steam, @ct, @t, @spec, @queue, 1) " +
            _dialect.OnConflict(new[] { "steam_id" }, Array.Empty<string>(), columns),
            ct, ("@steam", _dialect.SteamParam(steamId)), ("@ct", playtime.SecondsCt), ("@t", playtime.SecondsT),
            ("@spec", playtime.SecondsSpectator), ("@queue", playtime.SecondsQueue)).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task SetCountryAsync(long? sessionId, ulong steamId, string country, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        var code = Clip(country, CountryLength);
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, "UPDATE stats_players SET country = @country WHERE steam_id = @steam", ct,
            ("@country", code), ("@steam", _dialect.SteamParam(steamId))).ConfigureAwait(false);
        if (sessionId is { } id)
        {
            await ExecuteAsync(connection, transaction, "UPDATE stats_sessions SET country = @country WHERE id = @id", ct,
                ("@country", code), ("@id", id)).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task WritePresenceAsync(PresenceSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var at = _dialect.DateParam(snapshot.At);
        var serverId = await EnsureServerAsync(connection, transaction, at, touch: false, ct).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO stats_presence (server_id, at, humans, bots, queued, map_name) VALUES (@server, @at, @humans, @bots, @queued, @map)", ct,
            ("@server", serverId), ("@at", at), ("@humans", snapshot.Humans), ("@bots", snapshot.Bots), ("@queued", snapshot.Queued),
            ("@map", Clip(snapshot.MapName, NameLength))).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _serverId ??= serverId;
    }

    public async Task WriteServerEventsAsync(IReadOnlyList<ServerEvent> serverEvents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(serverEvents);
        if (serverEvents.Count == 0)
        {
            return;
        }
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var serverId = await EnsureServerAsync(connection, transaction, _dialect.DateParam(serverEvents[0].At), touch: false, ct).ConfigureAwait(false);
        foreach (var serverEvent in serverEvents)
        {
            await ExecuteAsync(connection, transaction,
                "INSERT INTO stats_server_events (server_id, at, type, module, value, occurrences, max_ms) VALUES (@server, @at, @type, @module, @value, @occurrences, @max_ms)", ct,
                ("@server", serverId), ("@at", _dialect.DateParam(serverEvent.At)), ("@type", Clip(serverEvent.Type, EventTypeLength)),
                ("@module", Clip(serverEvent.Module, NameLength)), ("@value", Clip(serverEvent.Value, ServerEventValueLength)),
                ("@occurrences", serverEvent.Occurrences), ("@max_ms", serverEvent.MaxMs)).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _serverId ??= serverId;
    }

    public async Task<long> WriteRoundAsync(long mapId, RoundSummary round, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(round);
        var record = round.Record;
        var context = record.Context;
        var sides = context.Participants.ToDictionary(p => p.SteamId, p => p.Side);
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Ascending SteamID: the same stats_players lock order as WriteReplayAsync, so the two never wait on each other in a cycle.
        foreach (var player in round.Players.OrderBy(p => p.SteamId))
        {
            await UpsertPlayerAsync(connection, transaction, player.SteamId, player.Name, context.StartedAt, ct).ConfigureAwait(false);
        }
        var roundId = await InsertAsync(connection, transaction, "stats_rounds", RoundColumns, RoundValues(mapId, record), ct).ConfigureAwait(false);
        await BulkInsertAsync(connection, transaction, "stats_round_players", RoundPlayerColumns, round.Players, p => RoundPlayerValues(roundId, p), ct).ConfigureAwait(false);
        await BulkInsertAsync(connection, transaction, "stats_shots", ShotColumns, record.Shots, s => new object?[]
        {
            roundId, _dialect.SteamParam(s.Shooter), Clip(s.Weapon, NameLength), s.TimeMs, s.Tick, s.Position?.X, s.Position?.Y, s.Position?.Z, s.Angles?.Pitch, s.Angles?.Yaw,
            s.Motion?.Speed, B(s.Motion?.Crouched), B(s.Motion?.Scoped), B(s.Motion?.Airborne), B(s.Motion?.Walking),
            s.Motion is { } motion && StatsWeapons.IsBullet(s.Weapon) ? (object)B(StatsWeapons.IsStill(s.Weapon, motion)) : null,
        }, ct).ConfigureAwait(false);
        await BulkInsertAsync(connection, transaction, "stats_player_events", EventColumns, record.Events, e => new object?[]
        {
            roundId, _dialect.SteamParam(e.Player), Clip(PlayerEvents.Name(e.Type), EventTypeLength), e.TimeMs, Clip(e.Value, EventValueLength),
            e.Position?.X, e.Position?.Y, e.Position?.Z,
        }, ct).ConfigureAwait(false);
        await BulkInsertAsync(connection, transaction, "stats_damages", DamageColumns, record.Damages, d => new object?[]
        {
            roundId, Steam(d.Attacker), _dialect.SteamParam(d.Victim), B(d.VictimIsBot), Clip(d.Weapon, NameLength), d.Hitgroup, d.DmgHealth, d.DmgArmor, d.HealthAfter, d.TimeMs, d.Tick,
        }, ct).ConfigureAwait(false);
        await BulkInsertAsync(connection, transaction, "stats_kills", KillColumns, record.Kills, k => new object?[]
        {
            roundId, Steam(k.Attacker), _dialect.SteamParam(k.Victim), B(k.VictimIsBot), Steam(k.Assister), B(k.FlashAssist), Clip(k.Weapon, NameLength), B(k.Headshot),
            k.Penetrated, B(k.NoScope), B(k.ThroughSmoke), B(k.AttackerBlind), B(k.AttackerInAir), k.Distance,
            k.AttackerPosition?.X, k.AttackerPosition?.Y, k.AttackerPosition?.Z, k.VictimPosition?.X, k.VictimPosition?.Y, k.VictimPosition?.Z,
            k.TimeMs, B(IsTeamKill(k, sides)),
        }, ct).ConfigureAwait(false);
        await BulkInsertAsync(connection, transaction, "stats_grenades", GrenadeColumns, round.Grenades, g => new object?[]
        {
            roundId, _dialect.SteamParam(g.Thrower), Clip(g.Type.ToString(), GrenadeTypeLength), g.ThrownMs, g.DetonatedMs,
            g.ThrowPosition?.X, g.ThrowPosition?.Y, g.ThrowPosition?.Z, g.DetonationPosition?.X, g.DetonationPosition?.Y, g.DetonationPosition?.Z,
            g.Damage, g.EnemiesBlinded, g.TeammatesBlinded, g.BlindSeconds,
        }, ct).ConfigureAwait(false);
        foreach (var player in round.Players)
        {
            await UpsertTotalsAsync(connection, transaction, context, player, record.Ranked, ct).ConfigureAwait(false);
            if (record.Ranked)
            {
                await UpdateEloAsync(connection, transaction, player, context.StartedAt, ct).ConfigureAwait(false);
            }
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return roundId;
    }

    // Lock order, the same on every server: the claimants' stats_players rows (ascending SteamID), then the stats_rounds
    // rows of every round the decisions touch (ascending id); WriteRoundAsync also takes its stats_players locks in
    // ascending SteamID and locks no existing round. A locking read of a round's owners happens only under that round's
    // lock (without a kept claim, the returned count is a plain read). A player's ownerships only change under that
    // player's lock and a round's owners, replay and tracks only under that round's lock, so two servers deciding for the same player or
    // the same round wait for each other, always in the same order (no deadlock). Every owner keeps a stats_players row:
    // claimants without one are ignored, and players are never deleted. MySQL / MariaDB run this transaction in READ
    // COMMITTED: each read sees what the previous lock holder committed, and the locking reads (owners count, replay row)
    // lock only the matching rows, never a gap. SQLite: Microsoft.Data.Sqlite begins with BEGIN IMMEDIATE (its default is
    // not deferred), so the database write lock is held from the start and the whole decision is serialized.
    public async Task<int> WriteReplayAsync(long roundId, ReplayRound replay, IReadOnlyList<ReplayClaim> claims, ReplayCaps caps, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(caps);
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        var claimants = new HashSet<ulong>();
        foreach (var steamId in claims.Select(c => c.SteamId).Distinct().Order())
        {
            var row = await ScalarAsync(connection, transaction, $"SELECT steam_id FROM stats_players WHERE steam_id = @steam{_dialect.ForUpdate}", ct,
                ("@steam", _dialect.SteamParam(steamId))).ConfigureAwait(false);
            if (row is not (null or DBNull))
            {
                claimants.Add(steamId);
            }
        }

        // Decisions first (no write yet), so that every round they touch is locked in one ascending pass.
        var held = new Dictionary<(ulong SteamId, ReplayReason Reason), List<ReplayOwnership>>();
        var kept = new List<(ReplayClaim Claim, IReadOnlyList<long> Evict)>();
        foreach (var claim in claims.Where(c => claimants.Contains(c.SteamId)).OrderBy(c => c.SteamId).ThenBy(c => c.Reason))
        {
            if (!held.TryGetValue((claim.SteamId, claim.Reason), out var owned))
            {
                owned = await OwnershipsAsync(connection, transaction, _dialect.SteamParam(claim.SteamId), ReasonName(claim.Reason), ct).ConfigureAwait(false);
                held[(claim.SteamId, claim.Reason)] = owned;
            }
            var decision = ReplaySelection.Decide(roundId, claim, owned, caps.For(claim.Reason));
            if (!decision.Keep)
            {
                continue;
            }
            owned.RemoveAll(o => decision.Evict.Contains(o.RoundId));
            owned.Add(new ReplayOwnership(roundId, claim.Score, at));
            kept.Add((claim, decision.Evict));
        }
        if (kept.Count == 0)
        {
            var current = await CountOwnersAsync(connection, transaction, roundId, locking: false, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return current;
        }

        var evicted = kept.SelectMany(k => k.Evict).Where(id => id != roundId).ToHashSet();
        foreach (var id in evicted.Append(roundId).Order())
        {
            await ScalarAsync(connection, transaction, $"SELECT id FROM stats_rounds WHERE id = @round{_dialect.ForUpdate}", ct, ("@round", id)).ConfigureAwait(false);
        }
        var when = _dialect.DateParam(at);
        foreach (var (claim, evict) in kept)
        {
            var steam = _dialect.SteamParam(claim.SteamId);
            var reason = ReasonName(claim.Reason);
            foreach (var old in evict)
            {
                await ExecuteAsync(connection, transaction,
                    "DELETE FROM stats_replay_owners WHERE round_id = @round AND steam_id = @steam AND reason = @reason", ct,
                    ("@round", old), ("@steam", steam), ("@reason", reason)).ConfigureAwait(false);
            }
            await ExecuteAsync(connection, transaction,
                "INSERT INTO stats_replay_owners (round_id, steam_id, reason, score, created_at) VALUES (@round, @steam, @reason, @score, @at)", ct,
                ("@round", roundId), ("@steam", steam), ("@reason", reason), ("@score", claim.Score), ("@at", when)).ConfigureAwait(false);
        }
        var owners = await CountOwnersAsync(connection, transaction, roundId, locking: true, ct).ConfigureAwait(false);
        if (owners > 0 && await ScalarAsync(connection, transaction, $"SELECT round_id FROM stats_replays WHERE round_id = @round{_dialect.ForUpdate}", ct,
                ("@round", roundId)).ConfigureAwait(false) is null or DBNull)
        {
            await ExecuteAsync(connection, transaction,
                "INSERT INTO stats_replays (round_id, sample_hz, created_at, bytes) VALUES (@round, @hz, @at, @bytes)", ct,
                ("@round", roundId), ("@hz", replay.SampleHz), ("@at", when), ("@bytes", replay.Bytes)).ConfigureAwait(false);
            await BulkInsertAsync(connection, transaction, "stats_replay_tracks", TrackColumns, replay.Tracks, t => new object?[]
            {
                roundId, _dialect.SteamParam(t.SteamId), t.Side.ToString(), t.Frames,
            }, ct).ConfigureAwait(false);
        }
        foreach (var old in evicted)
        {
            if (await CountOwnersAsync(connection, transaction, old, locking: true, ct).ConfigureAwait(false) == 0)
            {
                await ExecuteAsync(connection, transaction, "DELETE FROM stats_replay_tracks WHERE round_id = @round", ct, ("@round", old)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM stats_replays WHERE round_id = @round", ct, ("@round", old)).ConfigureAwait(false);
            }
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return owners;
    }

    private static string ReasonName(ReplayReason reason) => Clip(ReplayReasons.Name(reason), ReasonLength)!;

    private async Task<List<ReplayOwnership>> OwnershipsAsync(DbConnection connection, DbTransaction transaction, object steam, string reason, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT round_id, score, created_at FROM stats_replay_owners WHERE steam_id = @steam AND reason = @reason";
        AddParameter(command, "@steam", steam);
        AddParameter(command, "@reason", reason);
        var held = new List<ReplayOwnership>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            held.Add(new ReplayOwnership(
                Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture),
                _dialect.ReadDate(reader.GetValue(2))));
        }
        return held;
    }

    // locking (MySQL / MariaDB): a locking read of the round's latest committed owners, only while the round's
    // stats_rounds lock is held; otherwise a plain read.
    private async Task<int> CountOwnersAsync(DbConnection connection, DbTransaction transaction, long roundId, bool locking, CancellationToken ct) =>
        Convert.ToInt32(await ScalarAsync(connection, transaction,
            $"SELECT COUNT(*) FROM stats_replay_owners WHERE round_id = @round{(locking ? _dialect.ForUpdate : string.Empty)}", ct,
            ("@round", roundId)).ConfigureAwait(false), CultureInfo.InvariantCulture);

    public async Task<IReadOnlyDictionary<ulong, int>> LoadEloAsync(IReadOnlyCollection<ulong> steamIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(steamIds);
        var result = new Dictionary<ulong, int>();
        if (steamIds.Count == 0)
        {
            return result;
        }
        await using var connection = await _open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var names = steamIds.Select((_, i) => $"@s{i.ToString(CultureInfo.InvariantCulture)}").ToList();
        command.CommandText = $"SELECT steam_id, elo FROM stats_players WHERE steam_id IN ({string.Join(", ", names)})";
        foreach (var (steamId, index) in steamIds.Select((s, i) => (s, i)))
        {
            AddParameter(command, names[index], _dialect.SteamParam(steamId));
        }
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[_dialect.ReadSteam(reader.GetValue(0))] = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
        }
        return result;
    }

    // Rounds older than the cutoff lose their shots, damages and player events; presence snapshots and server events
    // older than the cutoff are deleted. Rounds, kills, grenades, sessions, replays and aggregates are kept.
    public async Task<int> PurgeRawAsync(DateTimeOffset olderThan, int batchSize, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        await using var connection = await _open(ct).ConfigureAwait(false);
        var cutoff = _dialect.DateParam(olderThan);
        var total = 0;
        var max = await ScalarAsync(connection, null, "SELECT MAX(id) FROM stats_rounds WHERE started_at < @cutoff", ct, ("@cutoff", cutoff)).ConfigureAwait(false);
        if (max is not (null or DBNull))
        {
            var maxRound = Convert.ToInt64(max, CultureInfo.InvariantCulture);
            foreach (var table in new[] { "stats_shots", "stats_damages", "stats_player_events" })
            {
                total += await DeleteAllAsync(connection, _dialect.DeleteBatch(table, batchSize), batchSize, ("@max_round", maxRound), ct).ConfigureAwait(false);
            }
        }
        foreach (var table in new[] { "stats_presence", "stats_server_events" })
        {
            total += await DeleteAllAsync(connection, _dialect.DeleteBatchBefore(table, batchSize), batchSize, ("@cutoff", cutoff), ct).ConfigureAwait(false);
        }
        return total;
    }

    private static async Task<int> DeleteAllAsync(DbConnection connection, string sql, int batchSize, (string Name, object? Value) parameter, CancellationToken ct)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await ExecuteAsync(connection, null, sql, ct, parameter).ConfigureAwait(false);
            total += deleted;
        }
        while (deleted == batchSize);
        return total;
    }

    private static bool IsTeamKill(PlayerKilled kill, IReadOnlyDictionary<ulong, TeamSide> sides) =>
        kill.Attacker is { } attacker && attacker != kill.Victim && !kill.VictimIsBot
        && sides.TryGetValue(attacker, out var attackerSide) && sides.TryGetValue(kill.Victim, out var victimSide) && attackerSide == victimSide;

    private static string? Clip(string? value, int max)
    {
        if (value is null || value.Length <= max)
        {
            return value;
        }
        // Never cut a surrogate pair in half.
        var length = char.IsHighSurrogate(value[max - 1]) ? max - 1 : max;
        return value[..length];
    }

    private static int B(bool value) => value ? 1 : 0;

    private static int? B(bool? value) => value is { } flag ? B(flag) : null;

    private object? Steam(ulong? steamId) => steamId is { } id ? _dialect.SteamParam(id) : null;

    private object?[] RoundValues(long mapId, RoundRecord record)
    {
        var context = record.Context;
        var site = context.Site?.ToString();
        var plant = record.Plant;
        var defuse = record.Defuse;
        return new object?[]
        {
            mapId, context.RoundNumber, site, Clip(context.RoundType, NameLength),
            context.Participants.Count(p => p.Side == TeamSide.CT), context.Participants.Count(p => p.Side == TeamSide.T),
            B(record.Ranked), record.Outcome.Winner?.ToString(), record.Outcome.EndReason, record.Outcome.DurationMs,
            B(plant is not null), plant is null ? null : site, plant is null ? null : _dialect.SteamParam(plant.Planter), plant?.TimeMs,
            B(defuse is not null), defuse is null ? null : _dialect.SteamParam(defuse.Defuser), B(defuse?.WithKit == true),
            B(record.Explosion is not null), _dialect.DateParam(context.StartedAt),
        };
    }

    // Movement columns stay NULL without movement stats (sampling off, no freeze end).
    private object?[] RoundPlayerValues(long roundId, PlayerRoundStats p)
    {
        var m = p.Movement;
        return new object?[]
        {
            roundId, _dialect.SteamParam(p.SteamId), p.Side.ToString(), Clip(p.PrimaryWeapon, NameLength), B(p.Survived), B(p.Won), p.Kills, p.Deaths, p.Assists,
            p.FlashAssists, p.TeamKills, p.Damage, p.UtilityDamage, p.HeadshotKills, p.Shots, p.Hits, p.HeadshotHits, B(p.EntryKill),
            B(p.EntryDeath), p.TradeKills, B(p.TradedDeath), B(p.Kast), p.ClutchVs, B(p.ClutchWon), p.EnemiesFlashed, p.TeammatesFlashed,
            p.BlindSecondsInflicted, B(p.Mvp), B(p.Planted), B(p.Defused), B(p.Defused && p.DefusedWithKit), p.Rating, p.EloBefore, p.EloAfter,
            p.ShotsStill, p.HitsStill, p.ShotsMoving, p.HitsMoving, p.ShotsScoped, p.HitsScoped, p.DamageTaken, p.BombCarryMs, p.HealthEnd, p.ArmorEnd,
            m?.AliveMs, m?.WalkPct, m?.RunPct, m?.CrouchPct, m?.StillPct, m?.AirPct, m?.DistanceMeters, m?.AfkMs,
            m?.InSmokePct, m?.BehindSmokePct, m?.FirstContactMs, m?.EngageOrder, B(m?.BaitFlag),
            p.NoscopeKills, p.WallbangKills, p.SmokeKills, p.BlindKills, p.AirKills,
        };
    }

    private Task UpsertPlayerAsync(DbConnection connection, DbTransaction transaction, ulong steamId, string name, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(connection, transaction,
            "INSERT INTO stats_players (steam_id, name, first_seen, last_seen, elo) VALUES (@steam, @name, @at, @at, @elo) " +
            _dialect.OnConflict(new[] { "steam_id" }, new[] { "name", "last_seen" }, Array.Empty<string>()),
            ct, ("@steam", _dialect.SteamParam(steamId)), ("@name", Clip(name, PlayerNameLength)), ("@at", _dialect.DateParam(at)), ("@elo", _options.EloStart));

    private async Task UpsertTotalsAsync(DbConnection connection, DbTransaction transaction, RoundContext context, PlayerRoundStats player, bool ranked, CancellationToken ct)
    {
        var counters = StatsCounters.ForAll(player, ranked);
        var counterParameters = StatsCounters.AllColumns.Select((_, i) => $"@c{i.ToString(CultureInfo.InvariantCulture)}").ToList();
        var totalsSql =
            $"INSERT INTO stats_player_totals (steam_id, scope, {string.Join(", ", StatsCounters.AllColumns)}) " +
            $"VALUES (@steam, @scope, {string.Join(", ", counterParameters)}) " +
            _dialect.OnConflict(new[] { "steam_id", "scope" }, Array.Empty<string>(), StatsCounters.AllColumns);
        foreach (var scope in StatsCounters.Scopes(context, player.Side))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = totalsSql;
            AddParameter(command, "@steam", _dialect.SteamParam(player.SteamId));
            AddParameter(command, "@scope", Clip(scope, ScopeLength));
            for (var i = 0; i < counters.Length; i++)
            {
                AddParameter(command, counterParameters[i], counters[i]);
            }
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var weaponSql =
            $"INSERT INTO stats_player_weapons (steam_id, weapon, {string.Join(", ", StatsCounters.WeaponColumns)}) " +
            "VALUES (@steam, @weapon, @w0, @w1, @w2, @w3, @w4, @w5) " +
            _dialect.OnConflict(new[] { "steam_id", "weapon" }, Array.Empty<string>(), StatsCounters.WeaponColumns);
        foreach (var weapon in player.Weapons)
        {
            var values = StatsCounters.ForWeapon(weapon);
            await ExecuteAsync(connection, transaction, weaponSql, ct, ("@steam", _dialect.SteamParam(player.SteamId)), ("@weapon", Clip(weapon.Weapon, NameLength)),
                ("@w0", values[0]), ("@w1", values[1]), ("@w2", values[2]), ("@w3", values[3]), ("@w4", values[4]), ("@w5", values[5])).ConfigureAwait(false);
        }
    }

    // Relative update: servers sharing the database never overwrite each other's deltas.
    private async Task UpdateEloAsync(DbConnection connection, DbTransaction transaction, PlayerRoundStats player, DateTimeOffset at, CancellationToken ct)
    {
        var steam = _dialect.SteamParam(player.SteamId);
        await ExecuteAsync(connection, transaction, $"UPDATE stats_players SET elo = {_dialect.Greatest}(@floor, elo + @delta) WHERE steam_id = @steam", ct,
            ("@floor", _options.EloFloor), ("@delta", player.EloDelta), ("@steam", steam)).ConfigureAwait(false);
        var elo = Convert.ToInt32(
            await ScalarAsync(connection, transaction, "SELECT elo FROM stats_players WHERE steam_id = @steam", ct, ("@steam", steam)).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO stats_elo_history (steam_id, day, elo, rounds, delta_sum) VALUES (@steam, @day, @elo, 1, @delta) " +
            _dialect.OnConflict(new[] { "steam_id", "day" }, new[] { "elo" }, new[] { "rounds", "delta_sum" }),
            ct, ("@steam", steam), ("@day", at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("@elo", elo), ("@delta", player.EloDelta))
            .ConfigureAwait(false);
    }

    private async Task<long> InsertAsync(DbConnection connection, DbTransaction transaction, string table, IReadOnlyList<string> columns, object?[] values, CancellationToken ct)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            var names = columns.Select((_, i) => $"@v{i.ToString(CultureInfo.InvariantCulture)}").ToList();
            command.CommandText = $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", names)})";
            for (var i = 0; i < values.Length; i++)
            {
                AddParameter(command, names[i], values[i]);
            }
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return Convert.ToInt64(await ScalarAsync(connection, transaction, _dialect.LastInsertId, ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task BulkInsertAsync<T>(
        DbConnection connection, DbTransaction transaction, string table, IReadOnlyList<string> columns, IReadOnlyList<T> rows, Func<T, object?[]> values, CancellationToken ct)
    {
        for (var start = 0; start < rows.Count; start += BatchRows)
        {
            var count = Math.Min(BatchRows, rows.Count - start);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var sql = new StringBuilder($"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ");
            for (var row = 0; row < count; row++)
            {
                var rowValues = values(rows[start + row]);
                sql.Append(row == 0 ? "(" : ", (");
                for (var column = 0; column < columns.Count; column++)
                {
                    var name = string.Create(CultureInfo.InvariantCulture, $"@p{row}_{column}");
                    sql.Append(column == 0 ? name : ", " + name);
                    AddParameter(command, name, rowValues[column]);
                }
                sql.Append(')');
            }
            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<int> ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

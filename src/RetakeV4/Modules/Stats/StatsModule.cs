using System.Diagnostics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Stats;
using RetakeV4.Domain.Teams;
using RetakeV4.Persistence;
using RetakeV4.Persistence.Stats;
using static RetakeV4.Modules.Stats.StatsEventMapper;

namespace RetakeV4.Modules.Stats;

// Translates CS2 events into stats facts. Decisions live in Domain/Stats; every write goes through StatsWriteQueue.
public sealed class StatsModule : IRetakeModule
{
    private const float PresenceRefreshSeconds = 5f;
    private const float UptimeSeconds = 600f;

    private readonly PlaytimeTracker _playtime = new();
    private readonly SessionActivityTracker _activity = new();
    private readonly ServerEventAggregator _serverEvents = new();
    private readonly Dictionary<int, string?> _primaryBySlot = new();
    private StatsConfig _config = new();
    private StatsRules? _rules;
    private RoundRecorder? _recorder;
    private EloCache? _elo;
    private IStatsRepository? _repository;
    private StatsWriteQueue? _writes;
    private MmdbCountryLookup? _countries;
    private FrameMonitor? _frames;
    private ModuleContext? _context;
    private TeamState _teams = TeamState.Empty;
    private float _roundStartTime;
    private long _loadedAt;
    private bool _missingPreparationWarned;

    // At server start the plugin loads before the engine globals exist (Teams publishes TeamStateChanged on load):
    // players are only queried once a map has started.
    private bool _mapStarted;
    private bool _scoreboardElo;

    public string Name => "Stats";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Stats module is not loaded");

    private static double Clock() => Environment.TickCount64 / 1000.0;

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("stats.json", new StatsConfig(), new StatsConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        _loadedAt = Stopwatch.GetTimestamp();
        _rules = _config.ToRules();
        _recorder = new RoundRecorder(_rules.MaxShotsPerRound, _rules.MinPlayersPerSide);
        _elo = new EloCache(_rules.Elo.Start, _rules.Elo.Floor);
        _frames = new FrameMonitor(_config.LongFrameMs);
        _repository = StatsRepositories.Create(_config.Database, context.Plugin.ModuleDirectory,
            new StatsWriteOptions(_config.Database.ServerKey, _rules.Elo.Start, _rules.Elo.Floor));
        if (_config.Database.Type == DatabaseType.Sqlite)
        {
            context.Logger.LogInformation("Stats are stored in SQLite: the web panel and external sites can only read them from MySql");
        }
        _countries = OpenCountries(context);
        var hooks = context.Hooks;
        hooks.OnBus<MapStarted>(e => OnMapStarted(e.MapName));
        hooks.OnBus<ModulesReady>(e =>
        {
            RecordStart(e.HotReload);
            if (e.HotReload)
            {
                RefreshPresence();
            }
        });
        hooks.OnBus<RoundPhaseChanged>(e =>
        {
            if (e.To == RoundPhase.Warmup)
            {
                _recorder?.Abandon();
            }
        });
        hooks.OnBus<LoadoutsAssigned>(OnLoadoutsAssigned);
        hooks.OnBus<RoundPrepared>(e => OpenRound(e.Context));
        hooks.OnBus<TeamStateChanged>(e =>
        {
            _teams = e.State;
            RefreshPresence();
        });
        hooks.OnEvent<EventRoundFreezeEnd>("stats_freeze_end", _ => _recorder?.MarkFreezeEnd(Now()));
        hooks.OnEvent<EventWeaponFire>("stats_weapon_fire", OnWeaponFire);
        hooks.OnEvent<EventWeaponReload>("stats_weapon_reload", OnWeaponReload);
        hooks.OnEvent<EventItemPickup>("stats_item_pickup", e => OnItem(e.Userid, PlayerEventType.Pickup, e.Item));
        hooks.OnEvent<EventItemRemove>("stats_item_remove", e => OnItem(e.Userid, PlayerEventType.Drop, e.Item));
        hooks.OnEvent<EventPlayerJump>("stats_player_jump", e => OnPlayerEvent(e.Userid, PlayerEventType.Jump));
        hooks.OnEvent<EventBombBeginplant>("stats_bomb_beginplant", e => OnPlayerEvent(e.Userid, PlayerEventType.PlantBegin));
        hooks.OnEvent<EventBombAbortplant>("stats_bomb_abortplant", e => OnPlayerEvent(e.Userid, PlayerEventType.PlantAbort));
        hooks.OnEvent<EventBombBegindefuse>("stats_bomb_begindefuse", e => OnPlayerEvent(e.Userid, PlayerEventType.DefuseBegin, e.Haskit ? "kit" : null));
        hooks.OnEvent<EventBombAbortdefuse>("stats_bomb_abortdefuse", e => OnPlayerEvent(e.Userid, PlayerEventType.DefuseAbort));
        hooks.OnEvent<EventBombPickup>("stats_bomb_pickup", e => OnPlayerEvent(e.Userid, PlayerEventType.BombPickup));
        hooks.OnEvent<EventBombDropped>("stats_bomb_dropped", e => OnPlayerEvent(e.Userid, PlayerEventType.BombDrop));
        hooks.OnEvent<EventPlayerHurt>("stats_player_hurt", OnPlayerHurt);
        hooks.OnEvent<EventPlayerDeath>("stats_player_death", OnPlayerDeath);
        hooks.OnEvent<EventGrenadeThrown>("stats_grenade_thrown", OnGrenadeThrown);
        hooks.OnEvent<EventHegrenadeDetonate>("stats_he_detonate", e => OnDetonated(e.Userid, GrenadeType.He, e.Entityid, e.X, e.Y, e.Z));
        hooks.OnEvent<EventFlashbangDetonate>("stats_flash_detonate", e => OnDetonated(e.Userid, GrenadeType.Flash, e.Entityid, e.X, e.Y, e.Z));
        hooks.OnEvent<EventSmokegrenadeDetonate>("stats_smoke_detonate", e => OnDetonated(e.Userid, GrenadeType.Smoke, e.Entityid, e.X, e.Y, e.Z));
        hooks.OnEvent<EventMolotovDetonate>("stats_molotov_detonate", e => OnDetonated(e.Userid, GrenadeType.Molotov, null, e.X, e.Y, e.Z));
        hooks.OnEvent<EventDecoyStarted>("stats_decoy_started", e => OnDetonated(e.Userid, GrenadeType.Decoy, e.Entityid, e.X, e.Y, e.Z));
        hooks.OnEvent<EventPlayerBlind>("stats_player_blind", OnPlayerBlind);
        hooks.OnEvent<EventBombPlanted>("stats_bomb_planted", e =>
        {
            if (HumanId(e.Userid) is { } planter)
            {
                _recorder?.Add(new BombPlantedFact(planter, Now()));
            }
        });
        hooks.OnEvent<EventBombDefused>("stats_bomb_defused", e =>
        {
            if (HumanId(e.Userid) is { } defuser)
            {
                _recorder?.Add(new BombDefusedFact(defuser, HasKit(e.Userid), Now()) { RemainingMs = BombRemainingMs() });
            }
        });
        hooks.OnEvent<EventBombExploded>("stats_bomb_exploded", _ => _recorder?.Add(new BombExplodedFact(Now())));
        hooks.OnEvent<EventRoundMvp>("stats_round_mvp", e =>
        {
            if (HumanId(e.Userid) is { } mvp)
            {
                _recorder?.Add(new MvpAwarded(mvp, e.Reason));
            }
        });
        hooks.OnEvent<EventRoundEnd>("stats_round_end", OnRoundEnd);
        hooks.OnEvent<EventPlayerConnectFull>("stats_connect", e => OnConnected(e.Userid));
        hooks.OnEvent<EventPlayerDisconnect>("stats_disconnect", e => OnDisconnected(e.Userid, e.Reason));
        _scoreboardElo = ScoreboardRank.IsAllowed(_config.ScoreboardElo, CoreConfig.FollowCS2ServerGuidelines);
        if (_config.ScoreboardElo && !_scoreboardElo)
        {
            context.Logger.LogWarning(
                "Stats: ScoreboardElo needs FollowCS2ServerGuidelines set to false in CounterStrikeSharp core.json; the scoreboard is left untouched");
        }
        if (_scoreboardElo)
        {
            // The game may reset the rank fields: they are written again on every spawn.
            hooks.OnEvent<EventPlayerSpawn>("stats_scoreboard_spawn", e => ShowElo(e.Userid));
        }
        if (_config.TrackServerHealth)
        {
            hooks.OnBus<ModuleFailureReported>(e => _serverEvents.Add(e));
            hooks.OnBus<SlowHandlerReported>(e => _serverEvents.Add(e));
            hooks.OnTick("stats_frame", OnFrame);
            hooks.RepeatTimer("stats_uptime", UptimeSeconds, RecordUptime);
        }
        if (_config.TrackUsage)
        {
            hooks.OnBus<PluginCommandUsed>(e => _serverEvents.Add(ServerEventTypes.Command, e.Module, e.Command));
            hooks.OnBus<MapVoteClosed>(OnMapVoteClosed);
            // Counted only: the message is never read.
            hooks.CommandListener("say", (player, _) => OnChat(player, "say"), HookMode.Pre);
            hooks.CommandListener("say_team", (player, _) => OnChat(player, "say_team"), HookMode.Pre);
        }
        if (_config.TrackServerHealth || _config.TrackUsage)
        {
            hooks.RepeatTimer("stats_server_events", ServerEventAggregator.FlushSeconds, FlushServerEvents);
        }
        if (_config.PresenceSnapshotSeconds > 0)
        {
            hooks.RepeatTimer("stats_presence_snapshot", _config.PresenceSnapshotSeconds, SnapshotPresence);
        }
        if (_config.PingSampleSeconds > 0)
        {
            hooks.RepeatTimer("stats_ping", _config.PingSampleSeconds, SamplePing);
        }
        hooks.RepeatTimer("stats_presence", PresenceRefreshSeconds, RefreshPresence);
        // Created last: it starts a background worker, which a throw during hook registration would leak.
        _writes = new StatsWriteQueue(_repository, context.Logger, _config.QueueCapacity,
            TimeSpan.FromSeconds(_config.ShutdownFlushSeconds));
    }

    public void Unload()
    {
        _recorder?.Abandon();
        var at = DateTimeOffset.UtcNow;
        FlushServerEvents();
        foreach (var (player, playtime) in _playtime.CloseAll(Clock()))
        {
            CloseSession(player, at, null, playtime);
        }
        _writes?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(_config.ShutdownFlushSeconds + 2));
        _writes = null;
        _repository = null;
        _recorder = null;
        _frames = null;
        _countries?.Dispose();
        _countries = null;
        _elo?.Clear();
        _elo = null;
        _mapStarted = false;
        ReleaseSqliteFiles();
        _context = null;
    }

    private int Now() => (int)Math.Round((Server.CurrentTime - _roundStartTime) * 1000);

    private void OnMapStarted(string mapName)
    {
        _mapStarted = true;
        _recorder?.Abandon();
        _frames?.Reset();
        _primaryBySlot.Clear();
        _missingPreparationWarned = false;
        var at = DateTimeOffset.UtcNow;
        foreach (var (player, playtime) in _playtime.CloseAll(Clock()))
        {
            CloseSession(player, at, null, playtime);
        }
        _writes?.Enqueue(new CloseMapCommand(at));
        _writes?.Enqueue(new OpenMapCommand(mapName, at));
        if (_config.RawRetentionDays > 0)
        {
            _writes?.Enqueue(new PurgeCommand(at.AddDays(-_config.RawRetentionDays)));
        }
    }

    private void OnLoadoutsAssigned(LoadoutsAssigned e)
    {
        _primaryBySlot.Clear();
        foreach (var (player, loadout) in e.Loadouts)
        {
            _primaryBySlot[player.Slot] = loadout.Primary is null ? null : StatsWeapons.Normalize(loadout.Primary);
        }
    }

    private void OpenRound(PreparationContext preparation)
    {
        if (_recorder is not { } recorder || _elo is not { } elo)
        {
            return;
        }
        var participants = new List<RoundParticipant>();
        var seen = new HashSet<ulong>();
        foreach (var player in PlayerQueries.Humans())
        {
            // First wins: a duplicate controller for one SteamID must not reach the analyzer twice.
            if (HumanId(player) is not { } id || PlayerQueries.SideOf(player) is not { } side || !seen.Add(id))
            {
                continue;
            }
            participants.Add(new RoundParticipant(
                id, player.PlayerName, side, elo.Current(id), _primaryBySlot.GetValueOrDefault(player.Slot)));
        }
        _roundStartTime = Server.CurrentTime;
        recorder.Open(new RoundContext(Server.MapName, preparation.RoundNumber, preparation.Site, preparation.RoundType, DateTimeOffset.UtcNow, participants));
        // The bomb handed out during the preparation (fast plant) fires no bomb_pickup while the round is open.
        foreach (var player in PlayerQueries.Humans())
        {
            if (HumanId(player) is { } carrier && CarriesBomb(player))
            {
                AddEvent(PlayerEvents.Simple(carrier, PlayerEventType.BombPickup, 0, PositionOf(player), "start"));
            }
        }
    }

    private void OnRoundEnd(EventRoundEnd e)
    {
        if (_recorder is not { } recorder)
        {
            return;
        }
        if (!recorder.IsOpen)
        {
            if (!_missingPreparationWarned && WinnerOf(e.Winner) is not null && GameRulesAccessor.Get()?.WarmupPeriod != true)
            {
                _missingPreparationWarned = true;
                Context.Logger.LogWarning("Stats: a round ended without being prepared; it is not recorded");
            }
            return;
        }
        if (WinnerOf(e.Winner) is not { } winner)
        {
            recorder.Abandon();
            return;
        }
        foreach (var player in PlayerQueries.Humans())
        {
            if (HumanId(player) is { } id)
            {
                recorder.Add(EndStateOf(id, player));
            }
        }
        var outcome = new RoundOutcome(winner, e.Reason, Now());
        // round_mvp follows round_end: close on the next frame so that the MVP is part of the round.
        Server.NextFrame(() => _context?.Guard.Run(Name, "stats_close_round", () => CloseRound(outcome)));
    }

    private void CloseRound(RoundOutcome outcome)
    {
        if (_recorder?.Close(outcome) is not { } record || _rules is not { } rules)
        {
            return;
        }
        var summary = RoundScoring.Process(record, rules);
        // The round closes a frame after round_end: a player who left meanwhile gets the delta from the database on return.
        _elo?.ApplyRound(summary.Players, _playtime.IsTracked);
        foreach (var player in PlayerQueries.Humans())
        {
            ShowElo(player);
        }
        Context.Bus.Publish(new RoundStatsRecorded(summary));
        _writes?.Enqueue(new WriteRoundCommand(summary));
    }

    private void OnWeaponFire(EventWeaponFire e)
    {
        if (_recorder is not { IsOpen: true } recorder || HumanId(e.Userid) is not { } shooter)
        {
            return;
        }
        var shot = new ShotFired(shooter, StatsWeapons.Normalize(e.Weapon), Now(), Server.TickCount, PositionOf(e.Userid), AnglesOf(e.Userid))
        {
            Motion = MotionOf(e.Userid),
        };
        if (!recorder.Add(shot) && recorder.ShotsDropped == 1)
        {
            Context.Logger.LogWarning("Stats: more than {Max} shots this round; further shots are not recorded", _rules?.MaxShotsPerRound);
        }
    }

    private void OnWeaponReload(EventWeaponReload e)
    {
        if (_recorder is not { IsOpen: true } || HumanId(e.Userid) is not { } id || ActiveWeaponOf(e.Userid) is not { } weapon)
        {
            return;
        }
        AddEvent(PlayerEvents.Reload(id, weapon.Weapon, weapon.Clip, weapon.MaxClip, Now(), PositionOf(e.Userid)));
    }

    private void OnItem(CCSPlayerController? player, PlayerEventType type, string item)
    {
        if (_recorder is { IsOpen: true } && HumanId(player) is { } id)
        {
            AddEvent(PlayerEvents.Item(id, type, item, Now(), PositionOf(player)));
        }
    }

    private void OnPlayerEvent(CCSPlayerController? player, PlayerEventType type, string? value = null)
    {
        if (_recorder is { IsOpen: true } && HumanId(player) is { } id)
        {
            AddEvent(PlayerEvents.Simple(id, type, Now(), PositionOf(player), value));
        }
    }

    private void AddEvent(PlayerEventFact? fact)
    {
        if (fact is null || _recorder is not { IsOpen: true } recorder)
        {
            return;
        }
        if (!recorder.Add(fact) && recorder.EventsDropped == 1)
        {
            Context.Logger.LogWarning("Stats: more than {Max} player events this round; further events are not recorded", _rules?.MaxShotsPerRound);
        }
    }

    private void OnPlayerHurt(EventPlayerHurt e)
    {
        if (_recorder is not { IsOpen: true } recorder || VictimOf(e.Userid) is not { } victim)
        {
            return;
        }
        recorder.Add(new DamageDealt(
            HumanId(e.Attacker), victim.Id, victim.IsBot, StatsWeapons.Normalize(e.Weapon), e.Hitgroup, e.DmgHealth, e.DmgArmor, e.Health, Now(), Server.TickCount));
    }

    private void OnPlayerDeath(EventPlayerDeath e)
    {
        if (_recorder is not { IsOpen: true } recorder || VictimOf(e.Userid) is not { } victim)
        {
            return;
        }
        recorder.Add(new PlayerKilled(
            HumanId(e.Attacker), victim.Id, victim.IsBot, HumanId(e.Assister), e.Assistedflash, StatsWeapons.Normalize(e.Weapon), e.Headshot,
            e.Penetrated, e.Noscope, e.Thrusmoke, e.Attackerblind, e.Attackerinair, e.Distance, PositionOf(e.Attacker), PositionOf(e.Userid), Now()));
    }

    private void OnGrenadeThrown(EventGrenadeThrown e)
    {
        if (HumanId(e.Userid) is { } thrower && StatsWeapons.GrenadeOf(e.Weapon) is { } type)
        {
            _recorder?.Add(new GrenadeThrown(thrower, type, Now(), PositionOf(e.Userid)));
        }
    }

    private void OnDetonated(CCSPlayerController? thrower, GrenadeType type, int? entityId, float x, float y, float z)
    {
        if (HumanId(thrower) is { } id)
        {
            _recorder?.Add(new GrenadeDetonated(id, type, entityId, Now(), new Vec3(x, y, z)));
        }
    }

    private void OnPlayerBlind(EventPlayerBlind e)
    {
        if (HumanId(e.Userid) is { } victim)
        {
            _recorder?.Add(new PlayerBlinded(HumanId(e.Attacker), victim, e.Entityid, e.BlindDuration, Now()));
        }
    }

    private void OnConnected(CCSPlayerController? player)
    {
        if (player is null || HumanId(player) is not { } id || _playtime.IsTracked(id))
        {
            return;
        }
        var (presence, vip) = PresenceOf(player);
        _playtime.Set(id, presence, Clock(), vip);
        _activity.Start(id);
        _writes?.Enqueue(new OpenSessionCommand(id, player.PlayerName, DateTimeOffset.UtcNow));
        LookUpCountry(id, player.IpAddress);
        LoadElo(id);
    }

    private void OnDisconnected(CCSPlayerController? player, int reason)
    {
        if (HumanId(player) is not { } id)
        {
            return;
        }
        _recorder?.Add(new PlayerLeftRound(id, Now()));
        if (_playtime.Close(id, Clock()) is { } playtime)
        {
            CloseSession(id, DateTimeOffset.UtcNow, reason, playtime);
        }
        _elo?.Forget(id);
    }

    private void CloseSession(ulong steamId, DateTimeOffset at, int? reason, Playtime playtime) =>
        _writes?.Enqueue(new CloseSessionCommand(steamId, at, reason, playtime) { Activity = _activity.Close(steamId) });

    // The lookup runs off the game thread; the address only reaches the reader and is never stored or logged.
    // CountryResolver.Resolve never throws (a disposed reader after an unload gives null). The result comes back through
    // the guard and is written only by the queue of this module load, after the OpenSessionCommand enqueued on connect.
    private void LookUpCountry(ulong steamId, string? address)
    {
        if (_countries is not { } lookup || _writes is not { } writes)
        {
            return;
        }
        _ = Task.Run(() =>
        {
            if (CountryResolver.Resolve(lookup, address) is not { } country)
            {
                return;
            }
            Server.NextFrame(() => _context?.Guard.Run(Name, "stats_country", () =>
            {
                if (ReferenceEquals(_writes, writes))
                {
                    writes.Enqueue(new PlayerCountryCommand(steamId, country));
                }
            }));
        });
    }

    // The file is relative to configs/plugins/RetakeV4 and must stay inside it (the validator already rejects "..").
    private MmdbCountryLookup? OpenCountries(ModuleContext context)
    {
        var root = Path.GetFullPath(Path.Combine(context.Plugin.ModuleDirectory, "..", "..", "configs", "plugins", "RetakeV4"));
        var path = Path.GetFullPath(Path.Combine(root, _config.GeoDatabaseFile));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            context.Logger.LogWarning("Stats: GeoDatabaseFile must stay inside the plugin config folder; player countries are not recorded");
            return null;
        }
        return MmdbCountryLookup.Open(path, context.Logger);
    }

    private void RefreshPresence()
    {
        if (!_mapStarted)
        {
            return;
        }
        var now = Clock();
        foreach (var player in PlayerQueries.Humans())
        {
            if (HumanId(player) is not { } id)
            {
                continue;
            }
            if (!_playtime.IsTracked(id))
            {
                OnConnected(player);
                continue;
            }
            var (presence, vip) = PresenceOf(player);
            _playtime.Set(id, presence, now, vip);
            if (_elo?.IsLoaded(id) == false)
            {
                LoadElo(id);
            }
        }
    }

    private (Presence Presence, bool Vip) PresenceOf(CCSPlayerController player) =>
        PresenceRules.Classify(_teams, new PlayerId(player.Slot), PlayerQueries.SideOf(player));

    private void SamplePing()
    {
        if (!_mapStarted)
        {
            return;
        }
        foreach (var player in PlayerQueries.Humans())
        {
            if (HumanId(player) is { } id)
            {
                _activity.AddPing(id, (int)Math.Min(player.Ping, 9999u));
            }
        }
    }

    private void SnapshotPresence()
    {
        if (!_mapStarted)
        {
            return;
        }
        _writes?.Enqueue(new PresenceCommand(new PresenceSnapshot(
            DateTimeOffset.UtcNow, Server.MapName, PlayerQueries.Humans().Count, BotCount(), _teams.Queue.Count)));
    }

    private void RecordStart(bool hotReload)
    {
        if (_config.TrackServerHealth)
        {
            _writes?.Enqueue(new ServerEventCommand(ServerEventFactory.Started(DateTimeOffset.UtcNow, hotReload, Context.Plugin.ModuleVersion)));
        }
    }

    private void RecordUptime() =>
        _writes?.Enqueue(new ServerEventCommand(ServerEventFactory.Uptime(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(_loadedAt))));

    private void OnFrame()
    {
        if (_frames?.Tick(Stopwatch.GetElapsedTime(0).TotalMilliseconds) is { } ms)
        {
            _serverEvents.Add(ServerEventTypes.LongFrame, null, null, ms);
        }
    }

    private HookResult OnChat(CCSPlayerController? player, string command)
    {
        if (HumanId(player) is not null)
        {
            _serverEvents.Add(ServerEventTypes.Chat, null, command);
        }
        return HookResult.Continue;
    }

    private void OnMapVoteClosed(MapVoteClosed e)
    {
        foreach (var row in ServerEventFactory.MapVoteRows(DateTimeOffset.UtcNow, e))
        {
            _writes?.Enqueue(new ServerEventCommand(row));
        }
    }

    private void FlushServerEvents()
    {
        foreach (var serverEvent in _serverEvents.Flush(DateTimeOffset.UtcNow))
        {
            _writes?.Enqueue(new ServerEventCommand(serverEvent));
        }
    }

    // Reads wait for the schema, run off the game thread, and come back through the guard. The token ties the result to
    // this connection (a load started before a disconnect is ignored), the cache instance to this module load.
    private void LoadElo(ulong steamId)
    {
        if (_repository is not { } repository || _writes is not { } writes || _elo is not { } cache || cache.BeginLoad(steamId) is not { } token)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                if (!await writes.Initialized.ConfigureAwait(false))
                {
                    OnGameThread("stats_elo_load_failed", cache, c => c.CancelLoad(steamId, token));
                    return;
                }
                var elo = await repository.LoadEloAsync(new[] { steamId }, CancellationToken.None).ConfigureAwait(false);
                OnGameThread("stats_elo_loaded", cache, c =>
                {
                    if (c.CompleteLoad(steamId, token, elo))
                    {
                        ShowElo(PlayerQueries.Humans().FirstOrDefault(p => p.SteamID == steamId));
                    }
                });
            }
            catch (Exception ex)
            {
                _context?.Logger.LogWarning(ex, "Stats: could not load the ELO of {SteamId}", steamId);
                OnGameThread("stats_elo_load_failed", cache, c => c.CancelLoad(steamId, token));
            }
        });
    }

    // Writes the scoreboard rank column only when it changes, then flags it for the network. A failure turns the feature
    // off until the next reload instead of counting against the module's error budget (it must never stop the stats).
    private void ShowElo(CCSPlayerController? player)
    {
        if (!_scoreboardElo || player is not { IsValid: true } || _elo is not { } elo)
        {
            return;
        }
        try
        {
            var rank = HumanId(player) is { } id ? ScoreboardRank.For(true, elo.IsLoaded(id), elo.Current(id)) : ScoreboardRank.None;
            if (player.CompetitiveRankType == rank.RankType && player.CompetitiveRanking == rank.Value)
            {
                return;
            }
            player.CompetitiveRankType = rank.RankType;
            player.CompetitiveRanking = rank.Value;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveRankType");
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveRanking");
        }
        catch (Exception ex)
        {
            _scoreboardElo = false;
            Context.Logger.LogWarning(ex, "Stats: writing the scoreboard rank failed; ScoreboardElo is off until the next reload");
        }
    }

    private void OnGameThread(string label, EloCache cache, Action<EloCache> action) =>
        Server.NextFrame(() => _context?.Guard.Run(Name, label, () =>
        {
            if (ReferenceEquals(_elo, cache))
            {
                action(cache);
            }
        }));

    private static (ulong Id, bool IsBot)? VictimOf(CCSPlayerController? victim)
    {
        if (IsBot(victim))
        {
            return (0UL, true);
        }
        return HumanId(victim) is { } id ? (id, false) : null;
    }

    // Pooled connections keep the .db file open; a host where the native SQLite library cannot load has nothing to release.
    private void ReleaseSqliteFiles()
    {
        if (_config.Database.Type != DatabaseType.Sqlite)
        {
            return;
        }
        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch (Exception ex) when (ex is TypeInitializationException or DllNotFoundException)
        {
            _context?.Logger.LogWarning(ex, "SQLite is not available on this server: nothing to release");
        }
    }
}

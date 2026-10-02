using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.MapVote;
using Vote = RetakeV4.Domain.MapVote.MapVote;

namespace RetakeV4.Modules.MapVote;

// Next-map vote near the end of the match (and on !rtv), then changelevel at match end or at round end after !rtv.
public sealed class MapVoteModule : IRetakeModule
{
    private MapVoteConfig _config = new();
    private ModuleContext? _context;
    private Vote? _vote;
    private DateTimeOffset _voteEndsAt;
    private bool _votedThisMap;
    private string? _nextMap;
    private RtvTracker _rtv = RtvTracker.Empty;
    private bool _rtvPassed;
    private bool _changeAtRoundEnd;
    private DateTimeOffset? _changeAt;
    private string? _changeTarget;

    public string Name => "MapVote";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core", "Hud" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("MapVote module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("mapvote.json", new MapVoteConfig(), new MapVoteConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnBus<MapStarted>(_ => Reset());
        hooks.OnEvent<EventRoundStart>("round_start", _ => OnRoundStart());
        hooks.OnEvent<EventRoundEnd>("round_end", _ =>
        {
            if (_changeAtRoundEnd && _nextMap is { } map)
            {
                ScheduleChange(map);
            }
        });
        hooks.OnEvent<EventCsWinPanelMatch>("match_end", _ =>
        {
            // The engine changes level on its own after mp_match_restart_delay: a vote still open is decided now.
            CloseVote();
            if (_nextMap is { } map)
            {
                ScheduleChange(map);
            }
        });
        hooks.OnEvent<EventPlayerConnectFull>("vote_join", e => ShowVote(e.Userid));
        hooks.OnEvent<EventPlayerDisconnect>("vote_leave", e => Left(e.Userid));
        hooks.OnBus<HudMenuSelected>(OnSelected);
        hooks.OnBus<MapVoteRequested>(OnRemoteRequest);
        hooks.Command("css_rtv", "Asks for a map change", (player, _) => OnRtv(player));
        hooks.Command("css_nextmap", "Shows the next map", (_, _) => OnNextMap());
        hooks.Command("css_mapvote", "Reopens the map vote menu", (player, _) => OnVoteCommand(player));
        hooks.RepeatTimer("tick", 1f, Tick);
    }

    public void Unload()
    {
        if (_vote is not null && _context is { } context)
        {
            CloseMenus(context);
        }
        _vote = null;
        _context = null;
    }

    private void Reset()
    {
        _vote = null;
        _votedThisMap = false;
        _nextMap = null;
        _rtv = RtvTracker.Empty;
        _rtvPassed = false;
        _changeAtRoundEnd = false;
        _changeAt = null;
        _changeTarget = null;
        _context?.Bus.Publish(new MapVoteStateChanged(false, null));
    }

    private static int MaxRounds() => ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 0;

    private void OnRoundStart()
    {
        if (VoteTrigger.ShouldOpen(MaxRounds(), GameRulesAccessor.TotalRoundsPlayed(), _config.TriggerRoundsBeforeEnd,
            GameRulesAccessor.IsWarmup(), _votedThisMap || _vote is not null))
        {
            OpenVote();
        }
    }

    private IReadOnlyList<string> Pool() => MapPool.Build(SpawnMaps(), Server.MapName, _config.ExcludedMaps, Server.IsMapValid);

    private void OpenVote()
    {
        var pool = Pool();
        _votedThisMap = true;
        if (pool.Count < MapPool.MinimumMaps)
        {
            Context.Logger.LogWarning("Map vote skipped on {Map}: {Count} map(s) available", Server.MapName, pool.Count);
            return;
        }
        _vote = Vote.Open(pool);
        Context.Bus.Publish(new MapVoteStateChanged(true, null));
        _voteEndsAt = DateTimeOffset.UtcNow.AddSeconds(_config.VoteSeconds);
        Context.Text.ChatAll("mapvote.vote.opened", _config.VoteSeconds);
        foreach (var player in PlayerQueries.Humans())
        {
            ShowVote(player);
        }
    }

    private IEnumerable<string> SpawnMaps()
    {
        var directory = Path.Combine(Context.Plugin.ModuleDirectory, "spawns");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList()
            : Array.Empty<string>();
    }

    private void ShowVote(CCSPlayerController? player)
    {
        if (_vote is not { } vote || player is not { IsValid: true, IsBot: false } || _context is not { } context)
        {
            return;
        }
        context.Bus.Publish(new HudMenuOpen(new PlayerId(player.Slot), MapVoteMenu.Build(vote.Maps, vote.Choice(player.Slot))));
    }

    private void OnSelected(HudMenuSelected e)
    {
        if (e.MenuId != MapVoteMenu.MenuId || _vote is not { } vote || MapVoteMenu.Parse(e.ItemId, vote.Maps) is not { } map)
        {
            return;
        }
        _vote = vote.Cast(e.Player.Slot, map);
        if (Utilities.GetPlayerFromSlot(e.Player.Slot) is { IsValid: true } player)
        {
            Context.Text.Chat(player, "mapvote.vote.cast", map);
        }
    }

    private void Left(CCSPlayerController? player)
    {
        if (player is null)
        {
            return;
        }
        _vote = _vote?.Remove(player.Slot);
        _rtv = _rtv.Left(player.Slot);
        if (GameRulesAccessor.IsWarmup())
        {
            return;
        }
        // The leaving player is still listed during the disconnect event.
        CheckRtv(PlayerQueries.Humans().Count(p => p.Slot != player.Slot));
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        if (_vote is not null && now >= _voteEndsAt)
        {
            CloseVote();
        }
        if (_changeAt is { } at && now >= at && _changeTarget is { } target)
        {
            _changeAt = null;
            ChangeLevel(target);
        }
    }

    private void CloseVote()
    {
        if (_vote is not { } vote)
        {
            return;
        }
        var (map, votes) = vote.Result(SystemRandom.Shared);
        CloseMenus(Context);
        _vote = null;
        _nextMap = map;
        Context.Text.ChatAll("mapvote.vote.result", map, votes);
        Context.Logger.LogInformation("Map vote: next map {Map} with {Votes} vote(s)", map, votes);
        Server.ExecuteCommand($"nextlevel {map}");
        Context.Bus.Publish(new MapVoteStateChanged(false, map));
    }

    private static void CloseMenus(ModuleContext context)
    {
        foreach (var player in PlayerQueries.Humans())
        {
            context.Bus.Publish(new HudMenuClose(new PlayerId(player.Slot), MapVoteMenu.MenuId));
        }
    }

    private void OnRemoteRequest(MapVoteRequested e)
    {
        var refusal = MapVoteRequestCheck.Check(_vote is not null, _nextMap is not null, Pool().Count);
        switch (refusal)
        {
            case MapVoteRequestRefusal.AlreadyOpen:
                e.Reply("Map vote already open");
                return;
            case MapVoteRequestRefusal.AlreadyDecided:
                e.Reply($"Map vote already decided: {_nextMap}");
                return;
            case MapVoteRequestRefusal.NotEnoughMaps:
                e.Reply("Map vote refused: fewer than 2 maps available");
                return;
        }
        // Same outcome as a successful !rtv: the voted map is played from the end of the current round.
        _rtvPassed = true;
        _changeAtRoundEnd = true;
        OpenVote();
        e.Reply($"Map vote opened ({_vote?.Maps.Count ?? 0} maps); the map changes at the end of the round");
    }

    private void OnRtv(CCSPlayerController? player)
    {
        if (player is not { IsValid: true })
        {
            return;
        }
        if (_rtvPassed)
        {
            Context.Text.Chat(player, "mapvote.rtv.already");
            return;
        }
        var humans = PlayerQueries.Humans().Count;
        var refusal = RtvTracker.Check(_config.RtvEnabled, GameRulesAccessor.IsWarmup(), humans, _config.RtvMinPlayers,
            GameRulesAccessor.TotalRoundsPlayed(), _config.RtvMinRounds, _nextMap is not null ? MapPool.MinimumMaps : Pool().Count);
        if (refusal is { } reason)
        {
            RefuseRtv(player, reason);
            return;
        }
        _rtv = _rtv.Want(player.Slot);
        Context.Text.ChatAll("mapvote.rtv.count", player.PlayerName, _rtv.Wanting.Count, RtvTracker.Needed(humans, _config.RtvPercentage));
        CheckRtv(humans);
    }

    private void RefuseRtv(CCSPlayerController player, RtvRefusal reason)
    {
        switch (reason)
        {
            case RtvRefusal.Disabled:
                Context.Text.Chat(player, "mapvote.rtv.disabled");
                break;
            case RtvRefusal.Warmup:
                Context.Text.Chat(player, "mapvote.rtv.warmup");
                break;
            case RtvRefusal.NotEnoughPlayers:
                Context.Text.Chat(player, "mapvote.rtv.not_enough_players", _config.RtvMinPlayers);
                break;
            case RtvRefusal.TooEarly:
                Context.Text.Chat(player, "mapvote.rtv.too_early", _config.RtvMinRounds);
                break;
            case RtvRefusal.NoMaps:
                Context.Text.Chat(player, "mapvote.rtv.no_maps");
                break;
        }
    }

    private void CheckRtv(int humans)
    {
        if (_rtvPassed || _rtv.Wanting.Count == 0 || !_rtv.IsReached(humans, _config.RtvPercentage))
        {
            return;
        }
        _rtvPassed = true;
        _changeAtRoundEnd = true;
        Context.Text.ChatAll("mapvote.rtv.passed");
        if (_nextMap is null && _vote is null)
        {
            OpenVote();
        }
    }

    private void OnNextMap()
    {
        if (_nextMap is { } map)
        {
            Context.Text.ChatAll("mapvote.nextmap", map);
            return;
        }
        Context.Text.ChatAll("mapvote.nextmap_none");
    }

    private void OnVoteCommand(CCSPlayerController? player)
    {
        if (player is not { IsValid: true })
        {
            return;
        }
        if (_vote is null)
        {
            Context.Text.Chat(player, "mapvote.vote.none_open");
            return;
        }
        ShowVote(player);
    }

    private void ScheduleChange(string map)
    {
        if (_changeAt is not null)
        {
            return;
        }
        if (!Server.IsMapValid(map))
        {
            // Logged once: forgetting the map stops every later round end from trying again.
            Context.Logger.LogError("Map vote: {Map} is no longer a valid map; no map change", map);
            _nextMap = null;
            _changeAtRoundEnd = false;
            Context.Bus.Publish(new MapVoteStateChanged(false, null));
            return;
        }
        _changeTarget = map;
        _changeAt = DateTimeOffset.UtcNow.AddSeconds(_config.ChangeDelaySeconds);
        Context.Text.ChatAll("mapvote.change.soon", map, _config.ChangeDelaySeconds);
    }

    private void ChangeLevel(string map)
    {
        Context.Logger.LogInformation("Map vote: changing to {Map}", map);
        Server.ExecuteCommand($"changelevel {map}");
    }
}

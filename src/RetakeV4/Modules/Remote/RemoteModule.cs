using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Remote;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Modules.Remote;

// Console-only commands for remote tools (Retake Deck over RCON): a JSON state line, map vote, cleanup replay.
public sealed class RemoteModule : IRetakeModule
{
    private RemoteConfig _config = new();
    private ModuleContext? _context;
    private string? _site;
    private string? _roundType;
    private int _queue;
    private bool _voteOpen;
    private string? _nextMap;
    private bool _spawnEditor;
    private bool _cleanupEditor;
    private RemoteForce? _force;
    private bool _scramblePending;

    public string Name => "Remote";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Remote module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("remote.json", new RemoteConfig(), new RemoteConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnBus<MapStarted>(_ => { _site = null; _roundType = null; _voteOpen = false; _nextMap = null; _force = null; _scramblePending = false; });
        hooks.OnBus<RoundPrepared>(e => { _site = e.Context.Site?.ToString(); _roundType = e.Context.RoundType; });
        hooks.OnBus<TeamStateChanged>(e => _queue = e.State.Queue.Count);
        hooks.OnBus<MapVoteStateChanged>(e => { _voteOpen = e.Open; _nextMap = e.NextMap; });
        hooks.OnBus<SpawnEditorStateChanged>(e => _spawnEditor = e.Active);
        hooks.OnBus<MapCleanupEditorStateChanged>(e => _cleanupEditor = e.Active);
        hooks.OnBus<SiteForceChanged>(e => _force = e.Force is { } f ? new RemoteForce(f.Site.ToString(), f.Mode == ForceSiteMode.Sticky) : null);
        hooks.OnBus<ScrambleStateChanged>(e => _scramblePending = e.Pending);
        hooks.Command("css_retake_state", "Prints the Retake state as one JSON line (console only)", (p, c) => ConsoleOnly(p, c, () => PrintState(c)));
        hooks.Command("css_retake_mapvote", "Opens the map vote (console only)", (p, c) => ConsoleOnly(p, c, () =>
            Request(c, "Map vote unavailable (module disabled or failed)", reply => new MapVoteRequested(reply))));
        hooks.Command("css_retake_cleanup", "Replays the map cleanup now (console only)", (p, c) => ConsoleOnly(p, c, () =>
            Request(c, "Map cleanup unavailable (module disabled or failed)", reply => new MapCleanupReplayRequested(reply))));
    }

    public void Unload() => _context = null;

    private void ConsoleOnly(CCSPlayerController? player, CommandInfo command, Action action)
    {
        if (player is not null)
        {
            if (player.IsValid)
            {
                Context.Text.ChatAlert(player, "remote.server_only");
            }
            return;
        }
        action();
    }

    // Polled every few seconds: a failure (map change in progress) answers an error line instead of counting against
    // the module's error budget.
    private void PrintState(CommandInfo command)
    {
        string line;
        try
        {
            line = ServerStateFormat.Format(Snapshot());
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ArgumentException)
        {
            line = "RETAKE_STATE_ERROR";
            Context.Logger.LogDebug(ex, "Remote state unavailable");
        }
        command.ReplyToCommand(line);
    }

    // The owner module answers synchronously; no answer means it is disabled or failed, and the caller must still get a line.
    private void Request<T>(CommandInfo command, string unavailable, Func<Action<string>, T> create) where T : notnull
    {
        var replied = false;
        Context.Bus.Publish(create(message =>
        {
            replied = true;
            command.ReplyToCommand(message);
        }));
        if (!replied)
        {
            command.ReplyToCommand(unavailable);
        }
    }

    private ServerStateSnapshot Snapshot()
    {
        var rules = GameRulesAccessor.Get();
        var phase = Context.Rounds.State.Phase.ToString();
        var scores = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager")
            .Where(t => t.IsValid)
            .GroupBy(t => (int)t.TeamNum)
            .ToDictionary(g => g.Key, g => g.First().Score);
        var players = Utilities.GetPlayers()
            .Where(p => p is { IsValid: true, IsHLTV: false } && p.Connected == PlayerConnectedState.Connected)
            .Select(p => new RemotePlayer(p.UserId ?? -1, p.IsBot ? string.Empty : p.SteamID.ToString(CultureInfo.InvariantCulture),
                p.PlayerName, TeamOf(p.TeamNum), p.PawnIsAlive, p.IsBot, p.PawnIsAlive ? Math.Max(0, p.PlayerPawn.Value?.Health ?? 0) : 0))
            .ToList();
        return new ServerStateSnapshot(
            Server.MapName, phase, rules?.WarmupPeriod ?? true, rules is { GamePaused: true } or { MatchWaitingForResume: true },
            (rules?.TotalRoundsPlayed ?? 0) + 1, ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 0,
            scores.GetValueOrDefault((int)CsTeam.Terrorist), scores.GetValueOrDefault((int)CsTeam.CounterTerrorist),
            _site, _roundType, players, _queue, _voteOpen, _nextMap, _spawnEditor, _cleanupEditor, _force, _scramblePending,
            RoundClock.TimeLeft(phase, rules?.RoundStartTime ?? 0f, rules?.RoundTime ?? 0, Server.CurrentTime, BombBlow(rules)),
            RoundClock.Bomb(rules?.BombPlanted ?? false, rules?.BombDefused ?? false));
    }

    private static float? BombBlow(CCSGameRules? rules)
    {
        if (rules is not { BombPlanted: true, BombDefused: false })
        {
            return null;
        }
        return Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(b => b.IsValid)?.C4Blow;
    }

    private static string TeamOf(int teamNum) => (CsTeam)teamNum switch
    {
        CsTeam.Terrorist => "T",
        CsTeam.CounterTerrorist => "CT",
        _ => "SPEC",
    };
}

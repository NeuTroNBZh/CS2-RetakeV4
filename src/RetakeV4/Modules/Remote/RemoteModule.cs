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
        hooks.OnBus<MapStarted>(_ => { _site = null; _roundType = null; _voteOpen = false; _nextMap = null; });
        hooks.OnBus<RoundPrepared>(e => { _site = e.Context.Site?.ToString(); _roundType = e.Context.RoundType; });
        hooks.OnBus<TeamStateChanged>(e => _queue = e.State.Queue.Count);
        hooks.OnBus<MapVoteStateChanged>(e => { _voteOpen = e.Open; _nextMap = e.NextMap; });
        hooks.OnBus<SpawnEditorStateChanged>(e => _spawnEditor = e.Active);
        hooks.OnBus<MapCleanupEditorStateChanged>(e => _cleanupEditor = e.Active);
        hooks.Command("css_retake_state", "Prints the Retake state as one JSON line (console only)", (p, c) => ConsoleOnly(p, c, () => c.ReplyToCommand(ServerStateFormat.Format(Snapshot()))));
        hooks.Command("css_retake_mapvote", "Opens the map vote (console only)", (p, c) => ConsoleOnly(p, c, () => Context.Bus.Publish(new MapVoteRequested(c.ReplyToCommand))));
        hooks.Command("css_retake_cleanup", "Replays the map cleanup now (console only)", (p, c) => ConsoleOnly(p, c, () => Context.Bus.Publish(new MapCleanupReplayRequested(c.ReplyToCommand))));
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

    private ServerStateSnapshot Snapshot()
    {
        var rules = GameRulesAccessor.Get();
        var scores = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager")
            .Where(t => t.IsValid)
            .GroupBy(t => (int)t.TeamNum)
            .ToDictionary(g => g.Key, g => g.First().Score);
        var players = Utilities.GetPlayers()
            .Where(p => p is { IsValid: true, IsHLTV: false } && p.Connected == PlayerConnectedState.Connected)
            .Select(p => new RemotePlayer(p.UserId ?? -1, p.PlayerName, TeamOf(p.TeamNum), p.PawnIsAlive, p.IsBot))
            .ToList();
        return new ServerStateSnapshot(
            Server.MapName, Context.Rounds.State.Phase.ToString(), rules?.WarmupPeriod ?? true, rules?.GamePaused ?? false,
            (rules?.TotalRoundsPlayed ?? 0) + 1, ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 0,
            scores.GetValueOrDefault((int)CsTeam.Terrorist), scores.GetValueOrDefault((int)CsTeam.CounterTerrorist),
            _site, _roundType, players, _queue, _voteOpen, _nextMap, _spawnEditor, _cleanupEditor);
    }

    private static string TeamOf(int teamNum) => (CsTeam)teamNum switch
    {
        CsTeam.Terrorist => "T",
        CsTeam.CounterTerrorist => "CT",
        _ => "SPEC",
    };
}

using System.Collections.Immutable;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Spawns;
using SpawnPoint = RetakeV4.Domain.Spawns.SpawnPoint;

namespace RetakeV4.Modules.Spawns;

public sealed class SpawnsModule : IRetakeModule
{
    private readonly IRandom _random = SystemRandom.Shared;
    private SpawnsConfig _config = new();
    private ModuleContext? _context;
    private IReadOnlyList<SpawnPoint> _spawns = Array.Empty<SpawnPoint>();
    private SiteHistory _history = SiteHistory.Empty;
    private ImmutableDictionary<int, SpawnPoint> _assignments = ImmutableDictionary<int, SpawnPoint>.Empty;

    public string Name => "Spawns";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Spawns module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("spawns.json", new SpawnsConfig(), new SpawnsConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnBus<MapStarted>(e => LoadSpawns(e.MapName));
        hooks.PreparationStep(new DelegatePreparationStep("site", PreparationOrder.Site, ChooseSite));
        hooks.PreparationStep(new DelegatePreparationStep("placement", PreparationOrder.Placement, PlacePlayers));
        hooks.OnEvent<EventPlayerSpawn>("player_spawn", OnPlayerSpawn);
        hooks.OnBus<RoundPhaseChanged>(e =>
        {
            if (e.To == RoundPhase.PostRound)
            {
                _assignments = ImmutableDictionary<int, SpawnPoint>.Empty;
            }
        });
        hooks.OnBus<RoundPrepared>(Announce);
    }

    public void Unload() => _context = null;

    private void LoadSpawns(string mapName)
    {
        _spawns = Array.Empty<SpawnPoint>();
        _history = SiteHistory.Empty;
        if (!MapNames.IsSafe(mapName))
        {
            Context.Logger.LogWarning("Map name {Map} is not a plain file name: default CS2 spawns will be used", mapName);
            return;
        }
        var path = Path.Combine(Context.Plugin.ModuleDirectory, "spawns", mapName + ".json");
        if (!File.Exists(path))
        {
            Context.Logger.LogWarning("No spawn file for {Map} at {Path}: default CS2 spawns will be used", mapName, path);
            return;
        }
        var result = SpawnFileFormat.Parse(File.ReadAllText(path));
        foreach (var issue in result.Issues)
        {
            Context.Logger.LogWarning("Spawn file {Map}: {Issue}", mapName, issue);
        }
        _spawns = result.Spawns;
        Context.Logger.LogInformation("Loaded {Count} spawns for {Map} (legacy format: {Legacy})", _spawns.Count, mapName, result.IsLegacyFormat);
    }

    private PreparationContext ChooseSite(PreparationContext context)
    {
        var available = _spawns.Select(s => s.Site).Distinct().ToList();
        var decision = SiteSelector.Choose(_history, null, _config.MaxSameSiteInRow, available, _random);
        _history = decision.History;
        return context with { Site = decision.Site };
    }

    private PreparationContext PlacePlayers(PreparationContext context)
    {
        if (context.Site is not { } site)
        {
            return context;
        }
        var humans = PlayerQueries.Humans();
        var requests = humans
            .Select(p => (Player: p, Side: PlayerQueries.SideOf(p)))
            .Where(p => p.Side is not null)
            .Select(p => new SpawnRequest(new PlayerId(p.Player.Slot), p.Side!.Value))
            .ToList();
        var result = SpawnSelector.Place(requests, _spawns, site, _random);
        _assignments = result.Assignments.ToImmutableDictionary(a => a.Player.Slot, a => a.Spawn);
        foreach (var player in humans.Where(p => _assignments.ContainsKey(p.Slot)))
        {
            Teleport(player, _assignments[player.Slot]);
        }
        if (result.Unplaced.Count > 0 && _spawns.Count > 0)
        {
            Context.Logger.LogWarning("{Count} player(s) kept the default spawn: not enough spawns on site {Site}", result.Unplaced.Count, site);
        }
        return context with { Planter = result.Planter };
    }

    private void OnPlayerSpawn(EventPlayerSpawn e)
    {
        var phase = Context.Rounds.State.Phase;
        if (e.Userid is not { IsValid: true } player || phase is not (RoundPhase.Preparing or RoundPhase.FreezeTime))
        {
            return;
        }
        if (_assignments.TryGetValue(player.Slot, out var spawn))
        {
            Teleport(player, spawn);
        }
    }

    private static void Teleport(CCSPlayerController player, SpawnPoint spawn)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
        {
            return;
        }
        pawn.Teleport(
            new Vector(spawn.Position.X, spawn.Position.Y, spawn.Position.Z),
            new QAngle(spawn.Angle.Pitch, spawn.Angle.Yaw, spawn.Angle.Roll),
            new Vector(0f, 0f, 0f));
    }

    private void Announce(RoundPrepared e)
    {
        if (e.Context.Site is { } site)
        {
            Context.Text.ChatAll("spawns.round.announce", e.Context.RoundType ?? "-", site.ToString());
        }
    }
}

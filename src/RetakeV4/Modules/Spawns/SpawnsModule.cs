using System.Collections.Immutable;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Spawns;
using SpawnPoint = RetakeV4.Domain.Spawns.SpawnPoint;

namespace RetakeV4.Modules.Spawns;

public sealed class SpawnsModule : IRetakeModule
{
    private const string AdminFlag = "@retakev4/admin";

    private readonly IRandom _random = SystemRandom.Shared;
    private SpawnsConfig _config = new();
    private ModuleContext? _context;
    private SiteHistory _history = SiteHistory.Empty;
    private ImmutableDictionary<int, SpawnPoint> _assignments = ImmutableDictionary<int, SpawnPoint>.Empty;
    private SpawnCatalog? _catalog;
    private SiteForce? _force;

    public string Name => "Spawns";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Spawns module is not loaded");

    private SpawnCatalog Catalog => _catalog ?? throw new InvalidOperationException("Spawns module is not loaded");

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
        _catalog = new SpawnCatalog(new SpawnFileStore(Path.Combine(context.Plugin.ModuleDirectory, "spawns")), context.Logger);
        var hooks = context.Hooks;
        hooks.OnBus<MapStarted>(e => LoadMap(e.MapName));
        hooks.Command("css_retake_forcesite", "Forces the bombsite: css_retake_forcesite <A|B|off> [once|sticky]", OnForceSite);
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
        hooks.OnBus<RoundPrepared>(e =>
        {
            Announce(e);
            WarnAdminsIfNoSpawns();
        });
    }

    public void Unload()
    {
        _catalog = null;
        _context = null;
    }

    private void LoadMap(string mapName)
    {
        _history = SiteHistory.Empty;
        _force = null;
        Catalog.Load(mapName);
    }

    private PreparationContext ChooseSite(PreparationContext context)
    {
        var available = Catalog.Set.Spawns.Select(s => s.Site).Distinct().ToList();
        var decision = SiteSelector.Choose(_history, _force, _config.MaxSameSiteInRow, available, _random);
        _history = decision.History;
        _force = decision.Force;
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
        var result = SpawnSelector.Place(requests, Catalog.Set.Spawns, site, _random);
        _assignments = result.Assignments.ToImmutableDictionary(a => a.Player.Slot, a => a.Spawn);
        foreach (var player in humans.Where(p => _assignments.ContainsKey(p.Slot)))
        {
            Teleport(player, _assignments[player.Slot]);
        }
        if (result.Unplaced.Count > 0 && Catalog.Set.Spawns.Count > 0)
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

    private void OnForceSite(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsAdmin(player))
        {
            Reply(player, "spawns.editor.no_permission");
            return;
        }
        if (ForceSiteCommand.Parse(command.GetArg(1), command.ArgCount > 2 ? command.GetArg(2) : null) is not { } request)
        {
            Reply(player, "spawns.forcesite.usage");
            return;
        }
        if (request.Force is not { } force)
        {
            _force = null;
            Reply(player, "spawns.forcesite.cleared");
            return;
        }
        var spawns = Catalog.Set.Spawns;
        if (spawns.Count > 0 && spawns.All(s => s.Site != force.Site))
        {
            Reply(player, "spawns.forcesite.no_spawns", force.Site.ToString());
            return;
        }
        _force = force;
        Reply(player, force.Mode == ForceSiteMode.Sticky ? "spawns.forcesite.set_sticky" : "spawns.forcesite.set_once", force.Site.ToString());
    }

    private void WarnAdminsIfNoSpawns()
    {
        if (Catalog.Set.Spawns.Count > 0 || Catalog.MapName is not { } map)
        {
            return;
        }
        foreach (var admin in PlayerQueries.Humans().Where(p => AdminManager.PlayerHasPermissions(p, AdminFlag)))
        {
            Context.Bus.Publish(new HudAlert(new PlayerId(admin.Slot), HudText.Of("spawns.missing.admin", map)));
        }
    }

    // The server console is always allowed.
    private static bool IsAdmin(CCSPlayerController? player) =>
        player is null || (player.IsValid && AdminManager.PlayerHasPermissions(player, AdminFlag));

    private void Reply(CCSPlayerController? player, string key, params object[] args)
    {
        if (player is { IsValid: true })
        {
            Context.Text.Chat(player, key, args);
            return;
        }
        Server.PrintToConsole(Context.Text.Server(key, args));
    }
}

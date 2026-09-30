using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;
using RetakeV4.Persistence;

namespace RetakeV4.Modules.Allocation;

public sealed class AllocationModule : IRetakeModule
{
    private const string RootFlag = "@retakev4/root";

    private readonly IRandom _random = SystemRandom.Shared;
    private readonly HashSet<string> _reportedMissingPools = new(StringComparer.Ordinal);
    private AllocationConfig _config = new();
    private GrenadesConfig _grenades = new();
    private ModuleContext? _context;
    private PreferenceService? _preferences;

    public string Name => "Allocation";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core", "RoundTypes" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Allocation module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var grenades = store.Load("grenades.json", new GrenadesConfig(), new GrenadesConfigValidator());
        ConfigLogging.Report(logger, grenades.Issues);
        _grenades = grenades.Config;
        var result = store.Load("allocation.json", new AllocationConfig(), new AllocationConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        _preferences = new PreferenceService(CreateStore(context), context.Logger);
        var hooks = context.Hooks;
        hooks.PreparationStep(new DelegatePreparationStep("loadout", PreparationOrder.Loadout, AssignLoadouts));
        hooks.OnEvent<EventPlayerConnectFull>("player_connect_full", e => OnConnected(e.Userid));
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { IsValid: true, IsBot: false } player)
            {
                _preferences?.PlayerDisconnected(player.SteamID);
            }
        });
        hooks.Command("css_awp", "Toggles AWP volunteering", OnAwpCommand);
        hooks.Command("css_retake_import_v3", "Imports V3 weapon preferences: css_retake_import_v3 <path to cs2retake.db>", OnImportCommand);
        foreach (var player in PlayerQueries.Humans())
        {
            OnConnected(player);
        }
    }

    public void Unload()
    {
        _preferences?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        _preferences = null;
        _context = null;
    }

    private IPreferenceRepository CreateStore(ModuleContext context)
    {
        var database = _config.Database;
        IPreferenceRepository repository = database.Type switch
        {
            DatabaseType.MySql => new MySqlPreferenceRepository(database.MySqlConnectionString),
            DatabaseType.Sqlite => new SqlitePreferenceRepository(Path.Combine(context.Plugin.ModuleDirectory, database.SqliteFile)),
            _ => new NoOpPreferenceRepository(),
        };
        var store = new ResilientPreferenceStore(repository, context.Logger, () => DateTimeOffset.UtcNow);
        _ = Task.Run(() => InitializeAsync(repository, context.Logger));
        return store;
    }

    private static async Task InitializeAsync(IPreferenceRepository repository, ILogger logger)
    {
        try
        {
            switch (repository)
            {
                case SqlitePreferenceRepository sqlite:
                    await sqlite.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
                case MySqlPreferenceRepository mySql:
                    await mySql.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Preference database could not be initialized; preferences will not be saved until it becomes available");
        }
    }

    private void OnConnected(CCSPlayerController? player)
    {
        if (player is { IsValid: true, IsBot: false, IsHLTV: false } && player.SteamID != 0)
        {
            _preferences?.PlayerConnected(player.SteamID, apply => Server.NextFrame(() => _context?.Guard.Run(Name, "preferences_loaded", apply)));
        }
    }

    private void OnAwpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true } || player.SteamID == 0 || _preferences is null)
        {
            return;
        }
        var optIn = _preferences.ToggleAwp(player.SteamID);
        Context.Text.Chat(player, optIn ? "allocation.awp.enabled" : "allocation.awp.disabled");
    }

    private void OnImportCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && !AdminManager.PlayerHasPermissions(player, RootFlag))
        {
            Context.Text.Chat(player, "allocation.no_permission");
            return;
        }
        var file = command.GetArg(1);
        var preferences = _preferences;
        if (preferences is null || string.IsNullOrWhiteSpace(file))
        {
            command.ReplyToCommand("usage: css_retake_import_v3 <path to cs2retake.db>");
            return;
        }
        _ = Task.Run(async () =>
        {
            string reply;
            try
            {
                reply = Context.Text.Server("allocation.import.done", await preferences.ImportV3Async(file, CancellationToken.None).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Context.Logger.LogWarning(ex, "V3 preference import from {File} failed", file);
                reply = Context.Text.Server("allocation.import.failed", ex.Message);
            }
            Server.NextFrame(() => _context?.Guard.Run(Name, "import_reply", () => Reply(player, reply)));
        });
    }

    // CommandInfo is only valid during the command callback: the delayed reply targets the player or the server console directly.
    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player is { IsValid: true })
        {
            player.PrintToChat(message);
            return;
        }
        Server.PrintToConsole(message);
    }

    private PreparationContext AssignLoadouts(PreparationContext context)
    {
        if (context.RoundTypeDefinition is not { } definition)
        {
            return context;
        }
        var players = PlayerQueries.Humans()
            .Select(p => (Controller: p, Side: PlayerQueries.SideOf(p)))
            .Where(p => p.Side is not null)
            .ToList();
        var requests = players
            .Select(p => new LoadoutRequest(
                new PlayerId(p.Controller.Slot),
                p.Side!.Value,
                _preferences?.RequestFor(p.Controller.SteamID, p.Side.Value, definition.Name)))
            .ToList();
        var plan = LoadoutPlanner.Plan(definition, requests, GrenadeKits(definition.GrenadePool), _random);
        foreach (var (controller, _) in players)
        {
            LoadoutApplier.Apply(controller, plan[new PlayerId(controller.Slot)]);
        }
        if (_config.Debug)
        {
            Context.Logger.LogInformation("Round {Round}: {Count} loadout(s) applied for {RoundType}", context.RoundNumber, plan.Count, definition.Name);
        }
        return context;
    }

    private IReadOnlyList<GrenadeKit> GrenadeKits(string pool)
    {
        var kits = _grenades.KitsFor(pool);
        if (kits.Count == 0 && _reportedMissingPools.Add(pool))
        {
            Context.Logger.LogWarning("Grenade pool {Pool} is empty or missing in grenades.json: no grenades will be given", pool);
        }
        return kits;
    }
}

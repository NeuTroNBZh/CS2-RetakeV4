using System.Collections.Immutable;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;
using RetakeV4.Persistence;

namespace RetakeV4.Modules.Allocation;

public sealed class AllocationModule : IRetakeModule
{
    private const string RootFlag = "@retakev4/root";

    private static readonly string[] GunsAliases =
    {
        "guns", "gans", "gun", "g", "gns", "gnus", "weapon", "waepon", "weapons", "waepons", "waffen", "menu", "allocator", "select",
    };

    private readonly IRandom _random = SystemRandom.Shared;
    private readonly HashSet<string> _reportedMissingPools = new(StringComparer.Ordinal);
    private AllocationConfig _config = new();
    private GrenadesConfig _grenades = new();
    private ModuleContext? _context;
    private PreferenceService? _preferences;
    private NativeBuySelector? _nativeBuy;
    private readonly HashSet<ulong> _menuSeen = new();
    private readonly HashSet<int> _autoOpened = new();
    private ImmutableDictionary<PlayerId, Loadout> _lastPlan = ImmutableDictionary<PlayerId, Loadout>.Empty;
    private IReadOnlyList<RoundTypeDefinition> _definitions = Array.Empty<RoundTypeDefinition>();
    private RoundTypeDefinition? _current;
    private bool _roundTypeChanged;

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
            if (e.Userid is not { IsValid: true } player)
            {
                return;
            }
            _lastPlan = _lastPlan.Remove(new PlayerId(player.Slot));
            _autoOpened.Remove(player.Slot);
            if (!player.IsBot)
            {
                _preferences?.PlayerDisconnected(player.SteamID);
            }
        });
        hooks.Command("css_awp", "Toggles AWP volunteering", OnAwpCommand);
        hooks.Command("css_retake_import_v3", "Imports V3 weapon preferences: css_retake_import_v3 <path to cs2retake.db>", OnImportCommand);
        hooks.OnBus<RoundTypesLoaded>(e => _definitions = e.Definitions);
        hooks.OnBus<HudMenuSelected>(OnMenuSelected);
        hooks.OnBus<RoundPhaseChanged>(OnPhaseChanged);
        foreach (var alias in GunsAliases)
        {
            hooks.Command($"css_{alias}", "Opens the weapon menu", (player, _) => OnGunsCommand(player));
        }
        if (AllocationModes.UsesNativeBuy(_config.Mode))
        {
            var nativeBuy = new NativeBuySelector(context, () => _current, OnNativeBuy, RestoreGuns, () => context.Rounds.State.Phase);
            _nativeBuy = nativeBuy;
            hooks.CommandListener("buy", nativeBuy.OnBuy, HookMode.Pre);
            hooks.OnEvent<EventItemPickup>("item_pickup", nativeBuy.OnItemPickup);
        }
        if (_config.HowToIntervalMinutes > 0)
        {
            hooks.RepeatTimer("howto", _config.HowToIntervalMinutes * 60f, () => Context.Text.ChatAll(AllocationModes.HowToKey(_config.Mode)));
        }
        ApplyBuyCvars();
        foreach (var player in PlayerQueries.Humans())
        {
            OnConnected(player);
        }
    }

    public void Unload()
    {
        _preferences?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        _preferences = null;
        _nativeBuy = null;
        SqliteConnection.ClearAllPools();
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
        _ = Task.Run(() => store.InitializeAsync(CancellationToken.None));
        return store;
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
        var context = Context;
        var requester = player?.SteamID;
        _ = Task.Run(async () =>
        {
            string reply;
            try
            {
                reply = context.Text.Server("allocation.import.done", await preferences.ImportV3Async(file, CancellationToken.None).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(ex, "V3 preference import from {File} failed", file);
                reply = context.Text.Server("allocation.import.failed", ex.Message);
            }
            Server.NextFrame(() => _context?.Guard.Run(Name, "import_reply", () => Reply(requester, reply)));
        });
    }

    // CommandInfo and the controller are only valid during the command callback: the delayed reply looks the admin up again by SteamID.
    private static void Reply(ulong? requester, string message)
    {
        if (requester is null)
        {
            Server.PrintToConsole(message);
            return;
        }
        PlayerQueries.Humans().FirstOrDefault(p => p.SteamID == requester)?.PrintToChat(message);
    }

    private PreparationContext AssignLoadouts(PreparationContext context)
    {
        if (context.RoundTypeDefinition is not { } definition)
        {
            return context;
        }
        _roundTypeChanged = _current?.Name != definition.Name;
        _current = definition;
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
        _lastPlan = plan.ToImmutableDictionary();
        foreach (var (controller, _) in players)
        {
            LoadoutApplier.Apply(controller, plan[new PlayerId(controller.Slot)]);
            Context.Bus.Publish(new LoadoutApplied(new PlayerId(controller.Slot)));
        }
        if (_nativeBuy is not null)
        {
            _nativeBuy.ClearPending();
            ApplyBuyCvars();
            foreach (var (controller, _) in players)
            {
                LoadoutApplier.ResetCash(controller);
            }
        }
        foreach (var awp in plan.Where(p => p.Value.Primary == WeaponCatalog.Awp))
        {
            Context.Bus.Publish(new HudAlert(awp.Key, HudText.Of("allocation.awp.received")));
        }
        if (_config.Debug)
        {
            Context.Logger.LogInformation("Round {Round}: {Count} loadout(s) applied for {RoundType}", context.RoundNumber, plan.Count, definition.Name);
        }
        return context;
    }

    private void OnGunsCommand(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || player.SteamID == 0)
        {
            return;
        }
        if (!AllocationModes.UsesMenu(_config.Mode))
        {
            Context.Text.Chat(player, AllocationModes.HowToKey(_config.Mode));
            return;
        }
        _menuSeen.Add(player.SteamID);
        OpenMenu(player, refreshOnly: false);
    }

    private void OpenMenu(CCSPlayerController player, bool refreshOnly)
    {
        if (_preferences is not { } preferences || player.SteamID == 0)
        {
            return;
        }
        var steamId = player.SteamID;
        var state = new WeaponMenuState(
            _definitions,
            _current,
            PlayerQueries.SideOf(player),
            (team, roundType) => preferences.RequestFor(steamId, team, roundType),
            preferences.IsAwpVolunteer(steamId));
        Context.Bus.Publish(new HudMenuOpen(new PlayerId(player.Slot), WeaponMenu.Build(state), refreshOnly));
    }

    private void OnMenuSelected(HudMenuSelected e)
    {
        if (e.MenuId != WeaponMenu.MenuId || _preferences is not { } preferences)
        {
            return;
        }
        var player = Utilities.GetPlayerFromSlot(e.Player.Slot);
        if (player is not { IsValid: true } || player.SteamID == 0)
        {
            return;
        }
        if (e.ItemId == WeaponMenu.AwpItemId)
        {
            preferences.ToggleAwp(player.SteamID);
        }
        else if (WeaponMenuSelection.Parse(e.ItemId) is { } selection && WeaponMenu.IsAllowed(selection, _definitions))
        {
            ApplySelection(player, preferences, selection);
        }
        OpenMenu(player, refreshOnly: true);
    }

    private void ApplySelection(CCSPlayerController player, PreferenceService preferences, WeaponMenuSelection selection)
    {
        preferences.SetWeapon(player.SteamID, selection.Team, selection.RoundType, selection.Slot, selection.Weapon);
        var id = new PlayerId(player.Slot);
        var phase = Context.Rounds.State.Phase;
        if (_current is { } definition && _lastPlan.TryGetValue(id, out var loadout)
            && WeaponMenu.AppliesNow(selection, phase, player.PawnIsAlive, PlayerQueries.SideOf(player), definition.Name))
        {
            var request = new LoadoutRequest(id, selection.Team, preferences.RequestFor(player.SteamID, selection.Team, definition.Name));
            var adjusted = LoadoutPlanner.WithWeapons(loadout, definition, request);
            _lastPlan = _lastPlan.SetItem(id, adjusted);
            LoadoutApplier.SwapWeapons(player, loadout, adjusted);
            Context.Bus.Publish(new HudAlert(id, HudText.Of("allocation.menu.applied_now")));
            return;
        }
        Context.Bus.Publish(new HudAlert(id, HudText.Of("allocation.menu.applied_next_round")));
    }

    private void OnPhaseChanged(RoundPhaseChanged e)
    {
        if (e.To == RoundPhase.FreezeTime)
        {
            AutoOpenMenus();
        }
        else if (e.To == RoundPhase.Live)
        {
            CloseAutoOpenedMenus();
        }
    }

    // New players and every player after a round type change get the menu once, if the round offers a choice.
    private void AutoOpenMenus()
    {
        if (!_config.AutoOpenMenu || !AllocationModes.UsesMenu(_config.Mode) || _current is not { } current)
        {
            return;
        }
        foreach (var player in PlayerQueries.Humans())
        {
            if (player.SteamID == 0 || PlayerQueries.SideOf(player) is not { } side || !WeaponMenu.HasChoice(current, side))
            {
                continue;
            }
            var firstTime = _menuSeen.Add(player.SteamID);
            if (firstTime || _roundTypeChanged)
            {
                OpenMenu(player, refreshOnly: false);
                _autoOpened.Add(player.Slot);
            }
        }
    }

    private void CloseAutoOpenedMenus()
    {
        foreach (var slot in _autoOpened)
        {
            Context.Bus.Publish(new HudMenuClose(new PlayerId(slot), WeaponMenu.MenuId));
        }
        _autoOpened.Clear();
    }

    private void OnNativeBuy(CCSPlayerController player, BuyDecision decision)
    {
        if (_preferences is not { } preferences)
        {
            return;
        }
        var id = new PlayerId(player.Slot);
        switch (decision.Outcome)
        {
            case BuyOutcome.SetWeapon when decision.Selection is { } selection:
                ApplySelection(player, preferences, selection);
                break;
            case BuyOutcome.AwpVolunteer:
                if (!preferences.IsAwpVolunteer(player.SteamID))
                {
                    preferences.ToggleAwp(player.SteamID);
                }
                Context.Bus.Publish(new HudAlert(id, HudText.Of("allocation.buy.awp_volunteer")));
                break;
            default:
                Context.Bus.Publish(new HudAlert(id, HudText.Of("allocation.buy.not_available")));
                break;
        }
    }

    private void RestoreGuns(CCSPlayerController player)
    {
        if (player.PawnIsAlive && _lastPlan.TryGetValue(new PlayerId(player.Slot), out var loadout))
        {
            LoadoutApplier.ReplaceGuns(player, loadout);
        }
    }

    private void ApplyBuyCvars()
    {
        foreach (var (name, value) in AllocationModes.Cvars(_config.Mode))
        {
            Server.ExecuteCommand($"{name} {value}");
        }
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

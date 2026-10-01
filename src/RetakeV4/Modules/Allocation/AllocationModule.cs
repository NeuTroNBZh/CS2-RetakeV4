using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Modules.Allocation;

public sealed class AllocationModule : IRetakeModule
{
    private readonly IRandom _random = SystemRandom.Shared;
    private readonly HashSet<string> _reportedMissingPools = new(StringComparer.Ordinal);
    private AllocationConfig _config = new();
    private GrenadesConfig _grenades = new();
    private ModuleContext? _context;

    public string Name => "Allocation";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core", "RoundTypes" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Allocation module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var grenades = store.Load("grenades.json", new GrenadesConfig(), new GrenadesConfigValidator());
        ConfigLogging.Report(logger, grenades.Issues);
        _grenades = grenades.Config;
        var result = store.Load("allocation.json", new AllocationConfig());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        context.Hooks.PreparationStep(new DelegatePreparationStep("loadout", PreparationOrder.Loadout, AssignLoadouts));
    }

    public void Unload() => _context = null;

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
        var requests = players.Select(p => new LoadoutRequest(new PlayerId(p.Controller.Slot), p.Side!.Value, null)).ToList();
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

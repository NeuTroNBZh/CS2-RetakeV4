using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.RoundTypes;

namespace RetakeV4.Modules.RoundTypes;

public sealed class RoundTypesModule : IRetakeModule
{
    private RoundTypesConfig _config = new();

    public string Name => "RoundTypes";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("roundtypes.json", new RoundTypesConfig(), new RoundTypesConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        var definitions = _config.ToDefinitions();
        context.Hooks.PreparationStep(new RoundTypeStep(_config.ToRules(), definitions, SystemRandom.Shared));
        context.Hooks.OnBus<ModulesReady>(_ => context.Bus.Publish(new RoundTypesLoaded(definitions.Values.ToList())));
        if (_config.Debug)
        {
            context.Hooks.OnBus<RoundPrepared>(e =>
                context.Logger.LogInformation("Round {Round}: type {RoundType} (rounds played {Played})", e.Context.RoundNumber, e.Context.RoundType, e.Context.RoundsPlayed));
        }
    }

    public void Unload()
    {
    }
}

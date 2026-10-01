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
        context.Hooks.PreparationStep(new RoundTypeStep(_config.ToRules(), _config.ToDefinitions(), SystemRandom.Shared));
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

using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;

namespace RetakeV4.Modules.Hud;

public sealed class HudModule : IRetakeModule
{
    private HudConfig _config = new();
    private ModuleContext? _context;

    public string Name => "Hud";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("hud.json", new HudConfig(), new HudConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        LoadCenter(context);
    }

    public void Unload() => _context = null;

    private void LoadCenter(ModuleContext context)
    {
        var center = new CenterHud(_config, context.Text, () => DateTimeOffset.UtcNow);
        var hooks = context.Hooks;
        hooks.OnBus<RoundPrepared>(center.OnRoundPrepared);
        hooks.OnBus<TeamStateChanged>(center.OnTeams);
        hooks.OnBus<HudAlert>(center.OnAlert);
        hooks.OnTick("center_tick", center.Tick);
    }
}

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Modules;

public sealed class ModuleHooks
{
    private readonly BasePlugin _plugin;
    private readonly IEventBus _bus;
    private readonly PreparationPipeline _pipeline;
    private readonly ModuleGuard _guard;
    private readonly string _module;
    private readonly ModuleRegistrations _registrations;

    public ModuleHooks(BasePlugin plugin, IEventBus bus, PreparationPipeline pipeline, ModuleGuard guard, string module, ModuleRegistrations registrations)
    {
        _plugin = plugin;
        _bus = bus;
        _pipeline = pipeline;
        _guard = guard;
        _module = module;
        _registrations = registrations;
    }

    public void OnEvent<T>(string stage, Action<T> handler, HookMode mode = HookMode.Post) where T : GameEvent =>
        OnEventHook<T>(stage, e =>
        {
            handler(e);
            return HookResult.Continue;
        }, mode);

    public void OnEventHook<T>(string stage, Func<T, HookResult> handler, HookMode mode) where T : GameEvent
    {
        BasePlugin.GameEventHandler<T> wrapper = (e, _) => _guard.Run(_module, stage, () => handler(e), HookResult.Continue);
        _plugin.RegisterEventHandler(wrapper, mode);
        _registrations.Track(() => _plugin.DeregisterEventHandler(wrapper, mode));
    }

    public void OnMapStart(string stage, Action<string> handler)
    {
        Listeners.OnMapStart wrapper = map => _guard.Run(_module, stage, () => handler(map));
        _plugin.RegisterListener(wrapper);
        _registrations.Track(() => _plugin.RemoveListener(wrapper));
    }

    public void Command(string name, string description, Action<CCSPlayerController?, CommandInfo> handler)
    {
        CommandInfo.CommandCallback wrapper = (player, info) => _guard.Run(_module, $"cmd:{name}", () => handler(player, info));
        _plugin.AddCommand(name, description, wrapper);
        _registrations.Track(() => _plugin.RemoveCommand(name, wrapper));
    }

    public void CommandListener(string name, Func<CCSPlayerController?, CommandInfo, HookResult> handler, HookMode mode)
    {
        CommandInfo.CommandListenerCallback wrapper = (player, info) =>
            _guard.Run(_module, $"listener:{name}", () => handler(player, info), HookResult.Continue);
        _plugin.AddCommandListener(name, wrapper, mode);
        _registrations.Track(() => _plugin.RemoveCommandListener(name, wrapper, mode));
    }

    public void OnBus<T>(Action<T> handler) where T : notnull
    {
        var subscription = _bus.Subscribe<T>(_module, e => _guard.Run(_module, $"bus:{typeof(T).Name}", () => handler(e)));
        _registrations.Track(subscription.Dispose);
    }

    public void PreparationStep(IPreparationStep step)
    {
        var registration = _pipeline.Register(_module, step);
        _registrations.Track(registration.Dispose);
    }
}

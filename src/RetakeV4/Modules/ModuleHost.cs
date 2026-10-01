using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Modules;

namespace RetakeV4.Modules;

public sealed class ModuleHost
{
    private readonly IReadOnlyList<IRetakeModule> _modules;
    private readonly ILogger _logger;
    private readonly Action<string> _disableModule;
    private readonly List<IRetakeModule> _loaded = new();
    private readonly Dictionary<string, ModuleRegistrations> _registrations = new();

    public ModuleHost(IReadOnlyList<IRetakeModule> modules, ILogger logger, Action<string> disableModule)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(disableModule);
        _modules = modules;
        _logger = logger;
        _disableModule = disableModule;
    }

    public IReadOnlyList<string> LoadedModules => _loaded.Select(m => m.Name).ToList();

    public void Start(JsonConfigStore store, Func<IRetakeModule, ModuleRegistrations, ModuleContext> contextFor)
    {
        var plan = ModuleLoadPlanner.Plan(_modules.Select(m => Describe(m, store)).ToList());
        foreach (var skipped in plan.Skipped)
        {
            _logger.LogWarning("Module {Module} not loaded: {Reason} ({Detail})", skipped.Name, skipped.Reason, skipped.Detail);
        }
        foreach (var name in plan.Order)
        {
            TryLoad(_modules.First(m => m.Name == name), contextFor);
        }
    }

    public void Stop()
    {
        foreach (var module in Enumerable.Reverse(_loaded).ToList())
        {
            SafeUnload(module);
            Release(module);
        }
        _loaded.Clear();
        foreach (var failed in _registrations.Keys.ToList())
        {
            _registrations.Remove(failed, out var registrations);
            registrations?.Dispose();
        }
    }

    private ModuleDescriptor Describe(IRetakeModule module, JsonConfigStore store)
    {
        try
        {
            var config = module.LoadConfig(store, _logger);
            return new ModuleDescriptor(module.Name, module.DependsOn, config.Enabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module}: config could not be loaded, module disabled", module.Name);
            return new ModuleDescriptor(module.Name, module.DependsOn, false);
        }
    }

    private void TryLoad(IRetakeModule module, Func<IRetakeModule, ModuleRegistrations, ModuleContext> contextFor)
    {
        var failedDependency = module.DependsOn.FirstOrDefault(d => _loaded.All(l => l.Name != d));
        if (failedDependency is not null)
        {
            _logger.LogWarning("Module {Module} not loaded: dependency {Dependency} failed to load", module.Name, failedDependency);
            return;
        }
        var registrations = new ModuleRegistrations(module.Name, _logger);
        _registrations[module.Name] = registrations;
        try
        {
            module.Load(contextFor(module, registrations));
            _loaded.Add(module);
            _logger.LogInformation("Module {Module} loaded", module.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module} failed to load and is disabled", module.Name);
            // Its handlers stay registered but the guard turns them into no-ops: CounterStrikeSharp cannot unhook an event
            // registered before the game loop starts, so releasing now would leave the engine calling a freed delegate.
            _disableModule(module.Name);
            SafeUnload(module);
        }
    }

    private void Release(IRetakeModule module)
    {
        if (_registrations.Remove(module.Name, out var registrations))
        {
            registrations.Dispose();
        }
    }

    private void SafeUnload(IRetakeModule module)
    {
        try
        {
            module.Unload();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module} failed to unload", module.Name);
        }
    }
}

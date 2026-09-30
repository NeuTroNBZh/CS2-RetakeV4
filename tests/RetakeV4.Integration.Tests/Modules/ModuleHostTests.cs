using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Modules;

namespace RetakeV4.Integration.Tests.Modules;

public sealed class ModuleHostTests : IDisposable
{
    private sealed record FakeConfig : ModuleConfig;

    private sealed class FakeModule(string name, List<string> journal, bool enabled = true, bool throwOnLoad = false, bool throwOnConfig = false, params string[] deps) : IRetakeModule
    {
        public string Name { get; } = name;
        public IReadOnlyList<string> DependsOn { get; } = deps;

        public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger) =>
            throwOnConfig ? throw new InvalidOperationException("config") : new FakeConfig { Enabled = enabled };

        public void Load(ModuleContext context)
        {
            if (throwOnLoad)
            {
                throw new InvalidOperationException("load");
            }
            journal.Add($"load:{Name}");
        }

        public void Unload() => journal.Add($"unload:{Name}");
    }

    private readonly TempDirectory _dir = new();
    private readonly List<string> _journal = new();
    private readonly ListLogger _logger = new();

    public void Dispose() => _dir.Dispose();

    private ModuleHost Start(params IRetakeModule[] modules)
    {
        var host = new ModuleHost(modules, _logger);
        host.Start(new JsonConfigStore(_dir.Path), _ => null!);
        return host;
    }

    [Fact]
    public void Start_LoadsInDependencyOrder()
    {
        var host = Start(new FakeModule("Hud", _journal, deps: new[] { "Core" }), new FakeModule("Core", _journal));
        Assert.Equal(new[] { "load:Core", "load:Hud" }, _journal);
        Assert.Equal(new[] { "Core", "Hud" }, host.LoadedModules);
    }

    [Fact]
    public void DisabledModule_IsNotLoaded_AndIsLogged()
    {
        var host = Start(new FakeModule("Core", _journal), new FakeModule("Links", _journal, enabled: false));
        Assert.Equal(new[] { "Core" }, host.LoadedModules);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Links"));
    }

    [Fact]
    public void ModuleFailingToLoad_IsUnloaded_AndDependentsAreSkipped()
    {
        var host = Start(new FakeModule("Core", _journal, throwOnLoad: true), new FakeModule("Hud", _journal, deps: new[] { "Core" }));
        Assert.Empty(host.LoadedModules);
        Assert.Contains("unload:Core", _journal);
        Assert.DoesNotContain("load:Hud", _journal);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Core"));
    }

    [Fact]
    public void ConfigFailure_DisablesTheModule()
    {
        var host = Start(new FakeModule("Core", _journal, throwOnConfig: true));
        Assert.Empty(host.LoadedModules);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void Stop_UnloadsInReverseOrder()
    {
        var host = Start(new FakeModule("Core", _journal), new FakeModule("Hud", _journal, deps: new[] { "Core" }));
        _journal.Clear();
        host.Stop();
        Assert.Equal(new[] { "unload:Hud", "unload:Core" }, _journal);
        Assert.Empty(host.LoadedModules);
    }
}

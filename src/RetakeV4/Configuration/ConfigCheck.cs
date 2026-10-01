using Microsoft.Extensions.Logging;
using RetakeV4.Localization;
using RetakeV4.Modules;
using RetakeV4.Modules.Spawns;

namespace RetakeV4.Configuration;

// Validates a server configuration (module configs, text overrides, spawns) the way the plugin would load it, without touching it.
public static class ConfigCheck
{
    public static IReadOnlyList<string> Run(string configDirectory, string? spawnsDirectory, string pluginLangDirectory)
    {
        var problems = new List<string>();
        var copy = Path.Combine(Path.GetTempPath(), $"retakev4-check-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(copy);
            if (Directory.Exists(configDirectory))
            {
                foreach (var file in Directory.GetFiles(configDirectory, "*.json"))
                {
                    File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
                }
            }
            var logger = new CollectingLogger();
            var store = new JsonConfigStore(copy);
            foreach (var module in ModuleCatalog.CreateAll())
            {
                module.LoadConfig(store, logger);
            }
            problems.AddRange(logger.Warnings);
        }
        finally
        {
            Directory.Delete(copy, true);
        }
        problems.AddRange(LangOverrideLoader.Read(configDirectory, pluginLangDirectory).Problems.Select(p => $"lang/{p}"));
        if (spawnsDirectory is not null && Directory.Exists(spawnsDirectory))
        {
            var store = new SpawnFileStore(spawnsDirectory);
            foreach (var map in Directory.GetFiles(spawnsDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal))
            {
                var load = store.Load(map);
                problems.AddRange(load.Issues.Select(i => $"spawns/{map}: {i}"));
                if (load.IsLegacy)
                {
                    problems.Add($"spawns/{map}: legacy format, convert it with RetakeV4.SpawnMigrator");
                }
            }
        }
        return problems;
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

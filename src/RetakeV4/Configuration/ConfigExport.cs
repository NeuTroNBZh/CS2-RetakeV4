using Microsoft.Extensions.Logging;
using RetakeV4.Modules;

namespace RetakeV4.Configuration;

// Writes each module's default config exactly as the plugin does on first start (existing files are kept).
public static class ConfigExport
{
    public static IReadOnlyList<string> Run(string directory, ILogger logger)
    {
        Directory.CreateDirectory(directory);
        var store = new JsonConfigStore(directory);
        foreach (var module in ModuleCatalog.CreateAll())
        {
            module.LoadConfig(store, logger);
        }
        return Directory.GetFiles(directory, "*.json")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}

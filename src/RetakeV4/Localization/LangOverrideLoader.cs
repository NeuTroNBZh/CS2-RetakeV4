using System.Text.Json;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Localization;

namespace RetakeV4.Localization;

// configs/plugins/RetakeV4/lang/<language>.json: partial replacements of the plugin texts, kept across updates.
public static class LangOverrideLoader
{
    public static TextOverrides Load(string configDirectory, string pluginLangDirectory, ILogger logger)
    {
        var (overrides, problems) = Read(configDirectory, pluginLangDirectory);
        foreach (var problem in problems)
        {
            logger.LogWarning("Text override ignored: {Problem}", problem);
        }
        if (overrides.Count > 0)
        {
            logger.LogInformation("Loaded {Count} text override(s)", overrides.Count);
        }
        return overrides;
    }

    public static (TextOverrides Overrides, IReadOnlyList<string> Problems) Read(string configDirectory, string pluginLangDirectory)
    {
        var folder = Path.Combine(configDirectory, "lang");
        if (!Directory.Exists(folder))
        {
            return (TextOverrides.Empty, Array.Empty<string>());
        }
        var problems = new List<string>();
        var byLanguage = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file), JsonConfigStore.SerializerOptions);
                byLanguage[Path.GetFileNameWithoutExtension(file)] = values ?? new Dictionary<string, string>();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        if (ReadReference(pluginLangDirectory, problems) is not { } reference)
        {
            return (TextOverrides.Empty, problems);
        }
        var (overrides, issues) = TextOverrides.Build(byLanguage, reference);
        problems.AddRange(issues.Select(i => $"{i.Language}.json [{i.Key}]: {i.Reason}"));
        return (overrides, problems);
    }

    // The plugin's own English texts: every override is checked against them (same keys and placeholders in every language).
    private static Dictionary<string, string>? ReadReference(string pluginLangDirectory, List<string> problems)
    {
        var file = Path.Combine(pluginLangDirectory, "en.json");
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file), JsonConfigStore.SerializerOptions)
                ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problems.Add($"plugin texts {file} could not be read ({ex.Message}); no override applied");
            return null;
        }
    }
}

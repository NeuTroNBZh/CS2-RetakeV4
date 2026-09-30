using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetakeV4.Configuration;

public sealed class JsonConfigStore
{
    private readonly string _directory;

    public JsonConfigStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConfigLoadResult<T> Load<T>(string fileName, T defaults, IConfigValidator<T>? validator = null)
        where T : ModuleConfig
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(defaults);
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            var writeIssue = TryWrite(path, fileName, defaults);
            return writeIssue is null
                ? new ConfigLoadResult<T>(defaults, Array.Empty<ConfigIssue>(), true)
                : new ConfigLoadResult<T>(defaults, new[] { writeIssue }, false);
        }

        var (config, readIssues) = Read(path, fileName, defaults);
        var versionIssues = config.Version < defaults.Version
            ? new[] { new ConfigIssue(fileName, nameof(ModuleConfig.Version), $"config version {config.Version} is older than {defaults.Version}; missing keys use defaults") }
            : Array.Empty<ConfigIssue>();
        var validated = validator?.Validate(config, defaults, fileName)
            ?? new ValidationResult<T>(config, Array.Empty<ConfigIssue>());
        var issues = readIssues.Concat(versionIssues).Concat(validated.Issues).ToList();
        return new ConfigLoadResult<T>(validated.Config, issues, false);
    }

    private static (T Config, IReadOnlyList<ConfigIssue> Issues) Read<T>(string path, string fileName, T defaults)
        where T : ModuleConfig
    {
        try
        {
            var config = JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions);
            return config is null
                ? (defaults, new[] { new ConfigIssue(fileName, "$", "document is null; using defaults") })
                : (config, Array.Empty<ConfigIssue>());
        }
        catch (JsonException ex)
        {
            var issue = new ConfigIssue(fileName, ex.Path ?? "$", $"invalid JSON ({ex.Message}); using defaults, file left untouched");
            return (defaults, new[] { issue });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (defaults, new[] { new ConfigIssue(fileName, "$", $"file could not be read ({ex.Message}); using defaults") });
        }
    }

    private static ConfigIssue? TryWrite<T>(string path, string fileName, T config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, SerializerOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConfigIssue(fileName, "$", $"default file could not be written ({ex.Message}); using defaults");
        }
    }
}

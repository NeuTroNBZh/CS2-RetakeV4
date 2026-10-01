using RetakeV4.Domain.MapCleanup;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Modules.MapCleanup;

// configs/plugins/RetakeV4/mapcleanup/<map>.json, written like the spawn files: temp file then move, previous version as .bak.
public sealed class CleanupOverrideStore
{
    private readonly string _directory;

    public CleanupOverrideStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public (IReadOnlyList<CleanupOverride> Overrides, IReadOnlyList<string> Issues) Load(string map)
    {
        var path = PathFor(map);
        if (!File.Exists(path))
        {
            return (Array.Empty<CleanupOverride>(), Array.Empty<string>());
        }
        var content = File.ReadAllText(path);
        var (overrides, issues, invalid) = CleanupOverridesFormat.Parse(content);
        if (!invalid)
        {
            return (overrides, issues);
        }
        var copy = $"{path}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.invalid.bak";
        File.WriteAllText(copy, content);
        return (Array.Empty<CleanupOverride>(), new[] { $"{Path.GetFileName(path)} is not valid JSON; copy kept as {Path.GetFileName(copy)}" });
    }

    public void Save(string map, IReadOnlyList<CleanupOverride> overrides)
    {
        var path = PathFor(map);
        Directory.CreateDirectory(_directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, CleanupOverridesFormat.Serialize(overrides));
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string map) =>
        MapNames.IsSafe(map) ? Path.Combine(_directory, map + ".json") : throw new ArgumentException($"Unsafe map name '{map}'", nameof(map));
}

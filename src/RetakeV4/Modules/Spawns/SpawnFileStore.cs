using RetakeV4.Domain.Spawns;

namespace RetakeV4.Modules.Spawns;

public sealed record SpawnLoad(IReadOnlyList<SpawnPoint> Spawns, bool Found, bool IsLegacy, IReadOnlyList<string> Issues);

public sealed class SpawnFileStore
{
    private readonly string _directory;

    public SpawnFileStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public SpawnLoad Load(string map)
    {
        var path = PathFor(map);
        if (!File.Exists(path))
        {
            return new SpawnLoad(Array.Empty<SpawnPoint>(), false, false, Array.Empty<string>());
        }
        var result = SpawnFileFormat.Parse(File.ReadAllText(path));
        return new SpawnLoad(result.Spawns, true, result.IsLegacyFormat, result.Issues);
    }

    // Written to a temp file then moved, so a failed write never truncates the spawn file. The previous version is kept as
    // <map>.json.bak, and the first V3 file overwritten is kept once as <map>.json.v3.bak.
    public void Save(string map, IReadOnlyList<SpawnPoint> spawns)
    {
        var path = PathFor(map);
        Directory.CreateDirectory(_directory);
        if (File.Exists(path))
        {
            var current = File.ReadAllText(path);
            var parsed = SpawnFileFormat.Parse(current);
            var legacyBackup = path + ".v3.bak";
            if (!File.Exists(legacyBackup) && parsed.IsLegacyFormat)
            {
                File.WriteAllText(legacyBackup, current);
            }
            // A file that did not load cleanly (hand-edit mistake) may hold spawns the editor never saw: it gets its own
            // timestamped copy that later saves never overwrite.
            if (parsed.Issues.Count > 0)
            {
                File.WriteAllText($"{path}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.invalid.bak", current);
            }
            File.WriteAllText(path + ".bak", current);
        }
        var temp = path + ".tmp";
        File.WriteAllText(temp, SpawnFileFormat.Serialize(map, spawns));
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string map)
    {
        if (!MapNames.IsSafe(map))
        {
            throw new ArgumentException($"Unsafe map name '{map}'", nameof(map));
        }
        return Path.Combine(_directory, map + ".json");
    }
}

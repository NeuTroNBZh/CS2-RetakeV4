using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Modules.Spawns;

// The spawns of the current map: what rounds use and what the editor changes. Game thread only.
public sealed class SpawnCatalog
{
    private static readonly SpawnSet Empty = SpawnSet.Loaded(Array.Empty<SpawnPoint>());

    private readonly SpawnFileStore _store;
    private readonly ILogger _logger;

    public SpawnCatalog(SpawnFileStore store, ILogger logger)
    {
        _store = store;
        _logger = logger;
    }

    public string? MapName { get; private set; }

    public SpawnSet Set { get; private set; } = Empty;

    public bool FileFound { get; private set; }

    public void Load(string mapName)
    {
        MapName = mapName;
        Set = Empty;
        FileFound = false;
        if (!MapNames.IsSafe(mapName))
        {
            _logger.LogWarning("Map name {Map} is not a plain file name: default CS2 spawns will be used", mapName);
            return;
        }
        SpawnLoad loaded;
        try
        {
            loaded = _store.Load(mapName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the spawn file of {Map}: default CS2 spawns will be used", mapName);
            return;
        }
        if (!loaded.Found)
        {
            _logger.LogWarning("No spawn file for {Map}: default CS2 spawns will be used", mapName);
            return;
        }
        foreach (var issue in loaded.Issues)
        {
            _logger.LogWarning("Spawn file {Map}: {Issue}", mapName, issue);
        }
        Set = SpawnSet.Loaded(loaded.Spawns);
        FileFound = true;
        _logger.LogInformation("Loaded {Count} spawns for {Map} (legacy format: {Legacy})", loaded.Spawns.Count, mapName, loaded.IsLegacy);
    }

    public void Replace(SpawnSet set) => Set = set;

    public void Reload()
    {
        if (MapName is { } map)
        {
            Load(map);
        }
    }

    // Returns null on success, otherwise a short reason for the admin.
    public string? Save()
    {
        if (MapName is not { } map || !MapNames.IsSafe(map))
        {
            return "no map loaded";
        }
        try
        {
            _store.Save(map, Set.Spawns);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the spawns of {Map}", map);
            return ex.Message;
        }
        Set = Set.MarkSaved();
        FileFound = true;
        _logger.LogInformation("Saved {Count} spawns for {Map}", Set.Spawns.Count, map);
        return null;
    }
}

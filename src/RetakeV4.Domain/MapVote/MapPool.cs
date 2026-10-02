using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.MapVote;

public static class MapPool
{
    public const int MinimumMaps = 2;

    public static IReadOnlyList<string> Build(IEnumerable<string> mapsWithSpawns, string? currentMap, IEnumerable<string> excluded, Func<string, bool> isValid)
    {
        var skip = new HashSet<string>(excluded.Select(m => m.Trim()), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(currentMap))
        {
            skip.Add(currentMap.Trim());
        }
        return mapsWithSpawns
            .Where(MapNames.IsSafe)
            .Where(m => !skip.Contains(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(isValid)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}

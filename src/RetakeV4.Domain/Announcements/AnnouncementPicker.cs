using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Announcements;

// Picks the next server message: the current map's list if it has one, else the general list; never the same twice in a row.
public sealed record AnnouncementPicker(
    IReadOnlyList<string> General,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ByMap,
    string? Last)
{
    public static AnnouncementPicker Create(IReadOnlyList<string> general, IReadOnlyDictionary<string, IReadOnlyList<string>> byMap)
    {
        ArgumentNullException.ThrowIfNull(general);
        ArgumentNullException.ThrowIfNull(byMap);
        var maps = byMap
            .Select(kv => (Map: kv.Key.Trim(), Messages: Usable(kv.Value)))
            .Where(m => m.Messages.Count > 0)
            .ToDictionary(m => m.Map, m => m.Messages, StringComparer.OrdinalIgnoreCase);
        return new AnnouncementPicker(Usable(general), maps, null);
    }

    public bool IsEmpty => General.Count == 0 && ByMap.Count == 0;

    public (AnnouncementPicker Next, string? Message) Pick(string map, IRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var pool = ByMap.TryGetValue(map ?? string.Empty, out var own) ? own : General;
        if (pool.Count == 0)
        {
            return (this, null);
        }
        var candidates = pool.Count > 1 ? pool.Where(m => m != Last).ToList() : pool.ToList();
        if (candidates.Count == 0)
        {
            candidates = pool.ToList();
        }
        var message = candidates[random.Next(candidates.Count)];
        return (this with { Last = message }, message);
    }

    private static IReadOnlyList<string> Usable(IReadOnlyList<string>? messages) =>
        (messages ?? Array.Empty<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
}

using System.Text.Json;

namespace RetakeV4.Domain.MapCleanup;

public sealed record CleanupOverride(string Key, CleanupKind Kind, string Note);

public static class CleanupOverridesFormat
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed record Row(string? Key, string? Kind, string? Note);

    public static string Serialize(IEnumerable<CleanupOverride> overrides) =>
        JsonSerializer.Serialize(overrides.Select(o => new Row(o.Key, o.Kind.ToString(), o.Note)).ToList(), Options);

    // Invalid = the whole file is unreadable (kept aside by the caller); a bad entry is only skipped and reported.
    public static (IReadOnlyList<CleanupOverride> Overrides, IReadOnlyList<string> Issues, bool Invalid) Parse(string json)
    {
        List<Row?>? rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<Row?>>(json, Options);
        }
        catch (JsonException)
        {
            return (Array.Empty<CleanupOverride>(), Array.Empty<string>(), true);
        }
        var kept = new List<CleanupOverride>();
        var issues = new List<string>();
        foreach (var (row, index) in (rows ?? new List<Row?>()).Select((r, i) => (r, i)))
        {
            if (string.IsNullOrWhiteSpace(row?.Key))
            {
                issues.Add($"entry {index}: no key");
                continue;
            }
            if (!Enum.TryParse<CleanupKind>(row.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || int.TryParse(row.Kind, out _))
            {
                issues.Add($"entry {index} ({row.Key}): unknown kind '{row.Kind}'");
                continue;
            }
            kept.Add(new CleanupOverride(row.Key, kind, row.Note ?? string.Empty));
        }
        return (kept, issues, false);
    }

    public static IReadOnlyDictionary<string, CleanupKind> ToMap(IEnumerable<CleanupOverride> overrides)
    {
        var map = new Dictionary<string, CleanupKind>(StringComparer.Ordinal);
        foreach (var o in overrides)
        {
            map[o.Key] = o.Kind;
        }
        return map;
    }
}

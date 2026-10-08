namespace RetakeV4.Domain.Compatibility;

public sealed record LegacyPlugin(string Folder, string DisplayName, string Replacement);

public static class LegacyPlugins
{
    private static readonly IReadOnlyList<LegacyPlugin> Known = new[]
    {
        new LegacyPlugin("CS2Retake", "CS2-RETAKE (V3)", "RetakeV4 replaces it (see docs/MIGRATION-V3.md)"),
        new LegacyPlugin("RetakeSpawnEditor", "CS2-SpawnEditor", "use the built-in editor: !retake edit"),
        new LegacyPlugin("breakerandopendoor", "CS2-BreakerAndOpenDoor", "use the MapCleanup module of RetakeV4"),
    };

    public static IReadOnlyList<LegacyPlugin> Detect(IEnumerable<string> pluginFolders)
    {
        ArgumentNullException.ThrowIfNull(pluginFolders);
        var installed = new HashSet<string>(pluginFolders, StringComparer.OrdinalIgnoreCase);
        return Known.Where(p => installed.Contains(p.Folder)).ToList();
    }

    public static string Describe(IReadOnlyList<LegacyPlugin> found) =>
        "Legacy plugin(s) detected next to RetakeV4: "
        + string.Join("; ", found.Select(p => $"{p.DisplayName} ({p.Replacement})"))
        + ". Remove them from addons/counterstrikesharp/plugins: running them together with RetakeV4 duplicates teams, spawns and weapons.";
}

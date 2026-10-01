using System.Text.RegularExpressions;

namespace RetakeV4.Domain.Spawns;

public static partial class MapNames
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,64}\z")]
    private static partial Regex SafeName();

    public static bool IsSafe(string? mapName) => mapName is not null && SafeName().IsMatch(mapName);
}

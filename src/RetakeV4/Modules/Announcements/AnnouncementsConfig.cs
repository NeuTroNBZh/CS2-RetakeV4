using RetakeV4.Configuration;

namespace RetakeV4.Modules.Announcements;

public sealed record AnnouncementsConfig : ModuleConfig
{
    public AnnouncementsConfig() => Version = 1;

    public int IntervalSeconds { get; init; } = 420;

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> MapMessages { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public string Welcome { get; init; } = string.Empty;
}

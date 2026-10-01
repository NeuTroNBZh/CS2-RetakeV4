using RetakeV4.Configuration;

namespace RetakeV4.Modules.Links;

public sealed record LinkConfig
{
    public IReadOnlyList<string> Commands { get; init; } = Array.Empty<string>();

    public string Message { get; init; } = string.Empty;
}

public sealed record LinksConfig : ModuleConfig
{
    public LinksConfig() => Version = 1;

    public IReadOnlyList<LinkConfig> Links { get; init; } = Array.Empty<LinkConfig>();
}

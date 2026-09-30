using RetakeV4.Configuration;

namespace RetakeV4.Modules.Spawns;

public sealed record SpawnsConfig : ModuleConfig
{
    public SpawnsConfig() => Version = 1;

    public int MaxSameSiteInRow { get; init; }
}

using RetakeV4.Configuration;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Persistence;

namespace RetakeV4.Modules.Allocation;

public sealed record AllocationConfig : ModuleConfig
{
    public AllocationConfig() => Version = 4;

    public DatabaseConfig Database { get; init; } = new();

    public bool AutoOpenMenu { get; init; } = true;

    public AllocationMode Mode { get; init; } = AllocationMode.Menu;

    public double HowToIntervalMinutes { get; init; } = 3.5;
}

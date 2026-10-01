using RetakeV4.Configuration;

namespace RetakeV4.Modules.Allocation;

public sealed record AllocationConfig : ModuleConfig
{
    public AllocationConfig() => Version = 1;
}

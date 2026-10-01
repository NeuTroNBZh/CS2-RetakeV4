using RetakeV4.Configuration;

namespace RetakeV4.Modules.Admin;

public sealed record AdminConfig : ModuleConfig
{
    public AdminConfig() => Version = 1;

    public bool SimpleAdminBridge { get; init; } = true;
}

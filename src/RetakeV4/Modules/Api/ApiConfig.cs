using RetakeV4.Configuration;

namespace RetakeV4.Modules.Api;

public sealed record ApiConfig : ModuleConfig
{
    public ApiConfig() => Version = 1;
}

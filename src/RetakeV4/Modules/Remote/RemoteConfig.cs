using RetakeV4.Configuration;

namespace RetakeV4.Modules.Remote;

public sealed record RemoteConfig : ModuleConfig
{
    public RemoteConfig() => Version = 1;
}

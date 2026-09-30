using RetakeV4.Configuration;

namespace RetakeV4.Modules.Core;

public sealed record CoreConfig : ModuleConfig
{
    public CoreConfig() => Version = 1;

    public string ExecConfig { get; init; } = "RetakeV4/retake.cfg";

    public float WarmupFallbackSeconds { get; init; } = 16f;

    public float WatchdogIntervalSeconds { get; init; } = 0.25f;
}

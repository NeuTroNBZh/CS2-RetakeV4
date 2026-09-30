using RetakeV4.Configuration;
using RetakeV4.Domain.InstaDefuse;

namespace RetakeV4.Modules.InstaDefuse;

public sealed record InstaDefuseConfig : ModuleConfig
{
    public InstaDefuseConfig() => Version = 1;

    public bool RequireNoTAlive { get; init; } = true;

    public bool BlockOnHe { get; init; } = true;

    public bool BlockOnMolotov { get; init; } = true;

    public bool BlockOnInferno { get; init; } = true;

    public float InfernoDistance { get; init; } = 250f;

    public bool ForceExplodeIfNoTime { get; init; } = true;

    public bool ChatNotification { get; init; } = true;

    public InstaDefuseRules ToRules() => new(RequireNoTAlive, BlockOnHe, BlockOnMolotov, BlockOnInferno, ForceExplodeIfNoTime);
}

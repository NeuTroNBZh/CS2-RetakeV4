using RetakeV4.Configuration;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Modules.MapCleanup;

public sealed record MapCleanupConfig : ModuleConfig
{
    public MapCleanupConfig() => Version = 1;

    public bool OpenDoors { get; init; } = true;

    public int DoorOpenChancePercent { get; init; } = 100;

    public bool BreakWindows { get; init; } = true;

    public bool BreakVents { get; init; } = true;

    public int MaxEntitiesPerRound { get; init; } = 512;

    public bool FreezeEndCheck { get; init; } = true;

    public CleanupSettings ToSettings() => new(OpenDoors, DoorOpenChancePercent, BreakWindows, BreakVents, MaxEntitiesPerRound);
}

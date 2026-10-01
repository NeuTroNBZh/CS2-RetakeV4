using RetakeV4.Configuration;

namespace RetakeV4.Modules.Plant;

public enum PlantMode
{
    AutoPlant,
    FastPlant,
}

public sealed record PlantConfig : ModuleConfig
{
    public PlantConfig() => Version = 1;

    public PlantMode Mode { get; init; } = PlantMode.AutoPlant;

    public float PlantCheckSeconds { get; init; } = 5f;
}

using RetakeV4.Modules.Plant;

namespace RetakeV4.Integration.Tests.Modules.Plant;

public class PlantConfigValidatorTests
{
    private static readonly PlantConfig Defaults = new();

    [Fact]
    public void Defaults_AreAutoPlantWithFiveSecondCheck()
    {
        var result = new PlantConfigValidator().Validate(Defaults, Defaults, "plant.json");
        Assert.Empty(result.Issues);
        Assert.Equal(PlantMode.AutoPlant, result.Config.Mode);
        Assert.Equal(5f, result.Config.PlantCheckSeconds);
    }

    [Fact]
    public void NegativeCheck_FallsBackToDefault()
    {
        var result = new PlantConfigValidator().Validate(Defaults with { PlantCheckSeconds = -1f }, Defaults, "plant.json");
        Assert.Equal(5f, result.Config.PlantCheckSeconds);
        Assert.Single(result.Issues);
    }
}

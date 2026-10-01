using RetakeV4.Modules.Spawns;

namespace RetakeV4.Integration.Tests.Modules.Spawns;

public class SpawnsConfigValidatorTests
{
    private static readonly SpawnsConfig Defaults = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = new SpawnsConfigValidator().Validate(Defaults, Defaults, "spawns.json");
        Assert.Empty(result.Issues);
        Assert.Equal(0, result.Config.MaxSameSiteInRow);
    }

    [Fact]
    public void NegativeMaxSameSiteInRow_FallsBackToDefault()
    {
        var result = new SpawnsConfigValidator().Validate(Defaults with { MaxSameSiteInRow = -2 }, Defaults, "spawns.json");
        Assert.Equal(0, result.Config.MaxSameSiteInRow);
        Assert.Single(result.Issues);
    }
}

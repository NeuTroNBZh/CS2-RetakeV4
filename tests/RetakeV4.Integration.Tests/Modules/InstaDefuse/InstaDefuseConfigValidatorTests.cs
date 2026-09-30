using RetakeV4.Modules.InstaDefuse;

namespace RetakeV4.Integration.Tests.Modules.InstaDefuse;

public class InstaDefuseConfigValidatorTests
{
    private static readonly InstaDefuseConfig Defaults = new();

    [Fact]
    public void Defaults_MatchV3()
    {
        var result = new InstaDefuseConfigValidator().Validate(Defaults, Defaults, "instadefuse.json");
        Assert.Empty(result.Issues);
        Assert.Equal(250f, result.Config.InfernoDistance);
        var rules = result.Config.ToRules();
        Assert.True(rules.RequireNoTAlive && rules.BlockOnHe && rules.BlockOnMolotov && rules.BlockOnInferno && rules.ForceExplodeIfNoTime);
    }

    [Fact]
    public void NegativeInfernoDistance_FallsBackToDefault()
    {
        var result = new InstaDefuseConfigValidator().Validate(Defaults with { InfernoDistance = -5f }, Defaults, "instadefuse.json");
        Assert.Equal(250f, result.Config.InfernoDistance);
        Assert.Single(result.Issues);
    }
}

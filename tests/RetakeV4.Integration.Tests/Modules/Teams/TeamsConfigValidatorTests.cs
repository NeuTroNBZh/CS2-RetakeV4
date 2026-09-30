using RetakeV4.Modules.Teams;

namespace RetakeV4.Integration.Tests.Modules.Teams;

public class TeamsConfigValidatorTests
{
    private static readonly TeamsConfig Defaults = new();
    private readonly TeamsConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid_AndMatchV3()
    {
        var result = _validator.Validate(Defaults, Defaults, "teams.json");
        Assert.Empty(result.Issues);
        var rules = result.Config.ToRules();
        Assert.Equal(9, rules.MaxPlayers);
        Assert.Equal(0.499, rules.TBalanceRatio);
        Assert.Equal(5, rules.ScrambleAfterTWins);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65)]
    public void OutOfRangeMaxPlayers_FallsBackToDefault(int maxPlayers)
    {
        var result = _validator.Validate(Defaults with { MaxPlayers = maxPlayers }, Defaults, "teams.json");
        Assert.Equal(9, result.Config.MaxPlayers);
        Assert.Single(result.Issues);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    public void OutOfRangeRatio_FallsBackToDefault(double ratio)
    {
        var result = _validator.Validate(Defaults with { TeamBalanceRatio = ratio }, Defaults, "teams.json");
        Assert.Equal(0.499, result.Config.TeamBalanceRatio);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void NegativeScrambleThreshold_FallsBackToDefault()
    {
        var result = _validator.Validate(Defaults with { ScrambleAfterTWins = -1 }, Defaults, "teams.json");
        Assert.Equal(5, result.Config.ScrambleAfterTWins);
    }

    [Fact]
    public void NullPriorityFlagEntries_AreRemoved_InsteadOfThrowing()
    {
        var flags = new PriorityFlagConfig?[] { null, new PriorityFlagConfig { Flag = null!, Priority = 1 }, new PriorityFlagConfig { Flag = "@css/vip", Priority = 1 } };
        var result = _validator.Validate(Defaults with { PriorityFlags = flags! }, Defaults, "teams.json");
        Assert.Equal(new[] { "@css/vip" }, result.Config.PriorityFlags.Select(f => f.Flag));
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void InvalidPriorityFlags_AreRemoved()
    {
        var flags = new[]
        {
            new PriorityFlagConfig { Flag = "@css/vip", Priority = 1 },
            new PriorityFlagConfig { Flag = "vip", Priority = 1 },
            new PriorityFlagConfig { Flag = "#retake/vip", Priority = 0 },
            new PriorityFlagConfig { Flag = "#retake/vip", Priority = 3 },
        };
        var result = _validator.Validate(Defaults with { PriorityFlags = flags }, Defaults, "teams.json");
        Assert.Equal(new[] { "@css/vip", "#retake/vip" }, result.Config.PriorityFlags.Select(f => f.Flag));
        Assert.Equal(2, result.Issues.Count);
    }
}

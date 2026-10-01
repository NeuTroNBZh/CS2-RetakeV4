using RetakeV4.Modules.MapVote;

namespace RetakeV4.Integration.Tests.Modules.MapVote;

public class MapVoteConfigValidatorTests
{
    private static readonly MapVoteConfig Defaults = new();
    private readonly MapVoteConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = _validator.Validate(Defaults, Defaults, "mapvote.json");
        Assert.Empty(result.Issues);
        Assert.Equal(3, result.Config.TriggerRoundsBeforeEnd);
        Assert.Equal(60, result.Config.RtvPercentage);
        Assert.Empty(result.Config.ExcludedMaps);
    }

    [Fact]
    public void OutOfRange_EachReplacedByDefault()
    {
        var bad = Defaults with
        {
            TriggerRoundsBeforeEnd = 0, VoteSeconds = 5, ChangeDelaySeconds = 60, RtvPercentage = 0, RtvMinPlayers = 0, RtvMinRounds = 31,
        };
        var result = _validator.Validate(bad, Defaults, "mapvote.json");
        Assert.Equal(6, result.Issues.Count);
        Assert.Equal(Defaults with { }, result.Config with { ExcludedMaps = Defaults.ExcludedMaps });
    }

    [Fact]
    public void NullExcludedMaps_BecomesEmpty()
    {
        Assert.Empty(_validator.Validate(Defaults with { ExcludedMaps = null! }, Defaults, "mapvote.json").Config.ExcludedMaps);
    }
}

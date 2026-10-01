using RetakeV4.Modules.Announcements;

namespace RetakeV4.Integration.Tests.Modules.Announcements;

public class AnnouncementsConfigValidatorTests
{
    private static readonly AnnouncementsConfig Defaults = new();
    private readonly AnnouncementsConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid_AndInactive()
    {
        var result = _validator.Validate(Defaults, Defaults, "announcements.json");
        Assert.Empty(result.Issues);
        Assert.Equal(420, result.Config.IntervalSeconds);
        Assert.Empty(result.Config.Messages);
        Assert.Empty(result.Config.MapMessages);
        Assert.Equal(string.Empty, result.Config.Welcome);
    }

    [Fact]
    public void IntervalBelow30_IsReported_AndDefaultUsed()
    {
        var result = _validator.Validate(Defaults with { IntervalSeconds = 5 }, Defaults, "announcements.json");
        Assert.Single(result.Issues);
        Assert.Equal(420, result.Config.IntervalSeconds);
    }

    [Fact]
    public void TooLongMessages_AreDropped_AndNullListsBecomeEmpty()
    {
        var config = Defaults with
        {
            Messages = new[] { "ok", new string('x', 513) },
            MapMessages = null!,
            Welcome = null!,
        };
        var result = _validator.Validate(config, Defaults, "announcements.json");
        Assert.Equal(new[] { "ok" }, result.Config.Messages);
        Assert.Empty(result.Config.MapMessages);
        Assert.Equal(string.Empty, result.Config.Welcome);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void SameMapWrittenTwice_IsMerged_AndReported()
    {
        var config = Defaults with
        {
            MapMessages = new Dictionary<string, IReadOnlyList<string>>
            {
                ["de_dust2"] = new[] { "a" },
                ["De_Dust2 "] = new[] { "b" },
            },
        };
        var result = _validator.Validate(config, Defaults, "announcements.json");
        Assert.Equal(new[] { "a", "b" }, Assert.Single(result.Config.MapMessages).Value);
        Assert.Single(result.Issues);
    }
}

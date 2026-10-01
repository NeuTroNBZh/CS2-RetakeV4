using RetakeV4.Modules.Links;

namespace RetakeV4.Integration.Tests.Modules.Links;

public class LinksConfigValidatorTests
{
    private static readonly LinksConfig Defaults = new();
    private readonly LinksConfigValidator _validator = new();

    private static LinkConfig Link(string message, params string[] commands) => new() { Commands = commands, Message = message };

    [Fact]
    public void Defaults_AreEmptyAndValid()
    {
        var result = _validator.Validate(Defaults, Defaults, "links.json");
        Assert.Empty(result.Issues);
        Assert.Empty(result.Config.Links);
    }

    [Fact]
    public void ValidLinks_AreKept_WithLowercaseCommands()
    {
        var result = _validator.Validate(Defaults with { Links = new[] { Link("Discord: https://discord.gg/x", "Discord", "dc") } }, Defaults, "links.json");
        Assert.Empty(result.Issues);
        Assert.Equal(new[] { "discord", "dc" }, Assert.Single(result.Config.Links).Commands);
    }

    [Fact]
    public void InvalidLinks_AreSkipped_AndTheRestIsKept()
    {
        var config = Defaults with
        {
            Links = new[]
            {
                Link("ok", "site"),
                Link("reserved", "guns"),
                Link("plugin prefix", "retake_info"),
                Link("bad name", "dis cord"),
                Link("duplicate", "site"),
                Link("   ", "rules"),
                Link(new string('x', 513), "long"),
                Link("no command"),
                null!,
            },
        };
        var result = _validator.Validate(config, Defaults, "links.json");
        Assert.Equal(new[] { "site" }, Assert.Single(result.Config.Links).Commands);
        Assert.Equal(8, result.Issues.Count);
    }

    [Fact]
    public void TooManyLinks_AreCapped()
    {
        var links = Enumerable.Range(0, 40).Select(i => Link("msg", $"link{i}")).ToList();
        var result = _validator.Validate(Defaults with { Links = links }, Defaults, "links.json");
        Assert.Equal(LinksConfigValidator.MaxLinks, result.Config.Links.Count);
        Assert.Equal(8, result.Issues.Count);
    }

    [Fact]
    public void MissingList_FallsBackToEmpty()
    {
        var result = _validator.Validate(Defaults with { Links = null! }, Defaults, "links.json");
        Assert.Empty(result.Config.Links);
        Assert.Single(result.Issues);
    }
}

using RetakeV4.Domain.Compatibility;

namespace RetakeV4.Domain.Tests.Compatibility;

public class LegacyPluginsTests
{
    [Fact]
    public void Detect_ReturnsNothing_WhenNoLegacyPluginIsInstalled() =>
        Assert.Empty(LegacyPlugins.Detect(new[] { "RetakeV4", "CS2-SimpleAdmin", "StatPlay" }));

    [Fact]
    public void Detect_FindsRetakeV3() =>
        Assert.Equal(new[] { "CS2-RETAKE (V3)" }, LegacyPlugins.Detect(new[] { "RetakeV4", "CS2Retake" }).Select(p => p.DisplayName));

    [Fact]
    public void Detect_FindsEveryLegacyPlugin()
    {
        var found = LegacyPlugins.Detect(new[] { "CS2Retake", "RetakeSpawnEditor", "breakerandopendoor" });
        Assert.Equal(3, found.Count);
    }

    [Theory]
    [InlineData("cs2retake")]
    [InlineData("CS2RETAKE")]
    [InlineData("BreakerAndOpenDoor")]
    public void Detect_IgnoresCase(string folder) =>
        Assert.Single(LegacyPlugins.Detect(new[] { folder }));

    [Fact]
    public void Detect_ReportsWhatToDo()
    {
        var plugin = Assert.Single(LegacyPlugins.Detect(new[] { "RetakeSpawnEditor" }));
        Assert.Contains("!retake edit", plugin.Replacement);
    }

    [Fact]
    public void Describe_NamesEachPluginAndTellsToRemoveIt()
    {
        var message = LegacyPlugins.Describe(LegacyPlugins.Detect(new[] { "CS2Retake", "breakerandopendoor" }));
        Assert.Contains("CS2-RETAKE (V3)", message);
        Assert.Contains("MapCleanup", message);
        Assert.Contains("Remove", message);
    }
}

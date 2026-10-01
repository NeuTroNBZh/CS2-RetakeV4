using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class MapNamesTests
{
    [Theory]
    [InlineData("de_mirage")]
    [InlineData("de_ancient_night")]
    [InlineData("cs-office_v2")]
    public void RegularNames_AreSafe(string name) => Assert.True(MapNames.IsSafe(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../server")]
    [InlineData("workshop/123/de_x")]
    [InlineData("de mirage")]
    [InlineData("C:\\maps\\x")]
    public void SuspiciousNames_AreRejected(string? name) => Assert.False(MapNames.IsSafe(name));

    [Fact]
    public void TooLongName_IsRejected() => Assert.False(MapNames.IsSafe(new string('a', 65)));
}

using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Integration.Tests.Spawns;

public class ShippedSpawnFilesTests
{
    private static readonly string SpawnDirectory = Path.Combine(AppContext.BaseDirectory, "spawns");

    public static IEnumerable<object[]> Maps() =>
        new[] { "de_ancient", "de_ancient_night", "de_anubis", "de_cache", "de_dust2", "de_inferno", "de_mirage", "de_nuke", "de_overpass", "de_train", "de_vertigo" }
            .Select(m => new object[] { m });

    [Theory]
    [MemberData(nameof(Maps))]
    public void ShippedFile_IsCurrentFormat_AndCoversBothSites(string map)
    {
        var result = SpawnFileFormat.Parse(File.ReadAllText(Path.Combine(SpawnDirectory, map + ".json")));
        Assert.False(result.IsLegacyFormat);
        Assert.Empty(result.Issues);
        foreach (var site in new[] { BombSite.A, BombSite.B })
        {
            Assert.Contains(result.Spawns, s => s.Site == site && s.Team == TeamSide.CT);
            Assert.Contains(result.Spawns, s => s.Site == site && s.Team == TeamSide.T && s.CanPlant);
        }
    }

    [Fact]
    public void Mirage_KeepsAllSpawnsFromV3()
    {
        var result = SpawnFileFormat.Parse(File.ReadAllText(Path.Combine(SpawnDirectory, "de_mirage.json")));
        Assert.Equal(52, result.Spawns.Count);
    }
}

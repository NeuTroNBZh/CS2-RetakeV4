using RetakeV4.Domain.MapVote;

namespace RetakeV4.Domain.Tests.MapVote;

public class MapPoolTests
{
    private static readonly string[] Spawns = { "de_nuke", "de_mirage", "de_dust2", "de_cache", "../evil" };

    [Fact]
    public void CurrentExcludedAndInvalidMaps_AreRemoved_RestSorted()
    {
        var pool = MapPool.Build(Spawns, "DE_NUKE", new[] { " de_cache " }, m => m != "de_dust2");
        Assert.Equal(new[] { "de_mirage" }, pool);
    }

    [Fact]
    public void AllValid_SortedAndDistinct()
    {
        var pool = MapPool.Build(new[] { "de_nuke", "de_ancient", "de_nuke" }, null, Array.Empty<string>(), _ => true);
        Assert.Equal(new[] { "de_ancient", "de_nuke" }, pool);
    }
}

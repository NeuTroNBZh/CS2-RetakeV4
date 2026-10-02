using RetakeV4.Domain.MapVote;

namespace RetakeV4.Domain.Tests.MapVote;

public class MapVoteRequestCheckTests
{
    [Theory]
    [InlineData(true, false, 10, MapVoteRequestRefusal.AlreadyOpen)]
    [InlineData(false, true, 10, MapVoteRequestRefusal.AlreadyDecided)]
    [InlineData(false, false, 1, MapVoteRequestRefusal.NotEnoughMaps)]
    public void Refuses(bool open, bool decided, int maps, MapVoteRequestRefusal expected) =>
        Assert.Equal(expected, MapVoteRequestCheck.Check(open, decided, maps));

    [Fact]
    public void Accepts() => Assert.Null(MapVoteRequestCheck.Check(false, false, MapPool.MinimumMaps));
}

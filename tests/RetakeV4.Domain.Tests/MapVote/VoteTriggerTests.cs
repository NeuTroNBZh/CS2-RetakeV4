using RetakeV4.Domain.MapVote;

namespace RetakeV4.Domain.Tests.MapVote;

public class VoteTriggerTests
{
    [Theory]
    [InlineData(30, 26, 3, false, false, false)]
    [InlineData(30, 27, 3, false, false, true)]
    [InlineData(30, 29, 3, false, false, true)]
    [InlineData(30, 27, 3, true, false, false)]
    [InlineData(30, 27, 3, false, true, false)]
    [InlineData(0, 100, 3, false, false, false)]
    public void OpensOnlyNearTheEnd(int max, int played, int before, bool warmup, bool voted, bool expected)
    {
        Assert.Equal(expected, VoteTrigger.ShouldOpen(max, played, before, warmup, voted));
    }
}

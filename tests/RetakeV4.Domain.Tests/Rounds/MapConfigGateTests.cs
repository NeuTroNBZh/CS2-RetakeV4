using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class MapConfigGateTests
{
    [Fact]
    public void FirstRoundStartAfterAMapStart_ExecutesTheConfigOnce()
    {
        var gate = MapConfigGate.Idle.MapStarted();
        var (afterFirst, first) = gate.RoundStarted();
        var (_, second) = afterFirst.RoundStarted();
        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void WithoutAMapStart_NothingIsExecuted() =>
        Assert.False(MapConfigGate.Idle.RoundStarted().Execute);

    [Fact]
    public void EachMapStart_ArmsTheGateAgain()
    {
        var (used, _) = MapConfigGate.Idle.MapStarted().RoundStarted();
        Assert.True(used.MapStarted().RoundStarted().Execute);
    }
}

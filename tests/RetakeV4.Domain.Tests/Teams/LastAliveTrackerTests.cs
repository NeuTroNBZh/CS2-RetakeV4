using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Teams;

public class LastAliveTrackerTests
{
    [Fact]
    public void ATeam_IsReportedOncePerRound()
    {
        var (tracker, first) = LastAliveTracker.Empty.Update(new TeamCount(1, 3), new TeamCount(2, 2));
        Assert.Equal(new[] { TeamSide.T }, first);
        var (_, again) = tracker.Update(new TeamCount(1, 3), new TeamCount(1, 2));
        Assert.Equal(new[] { TeamSide.CT }, again);
    }

    [Fact]
    public void SoloTeam_IsNeverReported()
    {
        var (_, newly) = LastAliveTracker.Empty.Update(new TeamCount(1, 1), new TeamCount(3, 3));
        Assert.Empty(newly);
    }

    [Fact]
    public void BothTeams_CanBeReportedTogether()
    {
        var (_, newly) = LastAliveTracker.Empty.Update(new TeamCount(1, 2), new TeamCount(1, 4));
        Assert.Equal(new[] { TeamSide.T, TeamSide.CT }, newly);
    }

    [Fact]
    public void NoOneOrSeveralAlive_IsNotReported()
    {
        var (tracker, newly) = LastAliveTracker.Empty.Update(new TeamCount(0, 3), new TeamCount(2, 3));
        Assert.Empty(newly);
        Assert.Same(LastAliveTracker.Empty, tracker);
    }
}

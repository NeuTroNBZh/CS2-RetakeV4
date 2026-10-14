using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Tests.Stats;

public class PresenceRulesTests
{
    private static readonly TeamState Teams = TeamState.Empty with
    {
        Queue = new[] { new QueuedPlayer(new PlayerId(3), 1, 1), new QueuedPlayer(new PlayerId(4), 0, 2) },
    };

    [Fact]
    public void QueuedWithPriority_IsVip() => Assert.Equal((Presence.Queue, true), PresenceRules.Classify(Teams, new PlayerId(3), null));

    [Fact]
    public void QueuedWithoutPriority_IsQueue_EvenOnATeam() =>
        Assert.Equal((Presence.Queue, false), PresenceRules.Classify(Teams, new PlayerId(4), TeamSide.CT));

    [Theory]
    [InlineData(TeamSide.CT, Presence.Ct)]
    [InlineData(TeamSide.T, Presence.T)]
    [InlineData(null, Presence.Spectator)]
    public void NotQueued_FollowsTheSide(TeamSide? side, Presence expected) =>
        Assert.Equal((expected, false), PresenceRules.Classify(Teams, new PlayerId(5), side));
}

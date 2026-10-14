using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class ReplayMemoryTests
{
    // Tracks exist for the given players only; ratings are offered for the same players.
    private static RememberedRound Round(long key, params ulong[] players) =>
        RememberedRound.Create(key, (int)key, "de_mirage", ReplayOf(players), players.ToDictionary(p => p, _ => 1.0));

    private static ReplayRound ReplayOf(params ulong[] players) =>
        new(8, players.Select(p => new ReplayTrack(p, TeamSide.CT, "[]", 0)).ToList());

    [Fact]
    public void Find_OneIsTheNewestRound()
    {
        var memory = new ReplayMemory(5);
        memory.Add(Round(1, Ct1));
        memory.Add(Round(2, Ct1));

        Assert.Equal(2, memory.TryFind(Ct1, 1).Round!.RoundKey);
        Assert.Equal(1, memory.TryFind(Ct1, 2).Round!.RoundKey);
    }

    [Fact]
    public void Add_BeyondTheCapacity_ForgetsTheOldest()
    {
        var memory = new ReplayMemory(2);
        memory.Add(Round(1, Ct1));
        memory.Add(Round(2, Ct1));
        memory.Add(Round(3, Ct1));

        Assert.Equal(2, memory.Count);
        Assert.Equal(SaveLookupStatus.NotFound, memory.TryFind(Ct1, 3).Status);
        Assert.Equal(2, memory.TryFind(Ct1, 2).Round!.RoundKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Find_OutOfRange_IsNotFound(int roundsAgo)
    {
        var memory = new ReplayMemory(5);
        memory.Add(Round(1, Ct1));
        memory.Add(Round(2, Ct1));

        Assert.Equal(SaveLookup.NotFound, memory.TryFind(Ct1, roundsAgo));
    }

    [Fact]
    public void Find_ARoundThePlayerDidNotPlay_IsNotFound()
    {
        var memory = new ReplayMemory(5);
        memory.Add(Round(1, Ct1));

        Assert.Equal(SaveLookupStatus.NotFound, memory.TryFind(T1, 1).Status);
    }

    [Fact]
    public void Create_KeepsRatingsOfPlayersWithATrackOnly()
    {
        var round = RememberedRound.Create(1, 1, "de_mirage", ReplayOf(Ct1), new Dictionary<ulong, double> { [Ct1] = 1.2, [T1] = 0.8 });

        Assert.Equal(new[] { Ct1 }, round.Ratings.Keys);
        Assert.Equal(1.2, round.Ratings[Ct1]);
    }

    [Fact]
    public void Find_AParticipantWithoutATrack_CannotOwnTheRound()
    {
        var memory = new ReplayMemory(5);
        memory.Add(RememberedRound.Create(1, 1, "de_mirage", ReplayOf(Ct1), new Dictionary<ulong, double> { [Ct1] = 1, [T1] = 1 }));

        Assert.Equal(SaveLookupStatus.NotFound, memory.TryFind(T1, 1).Status);
        Assert.Equal(SaveLookupStatus.Found, memory.TryFind(Ct1, 1).Status);
    }

    [Fact]
    public void TryFind_DoesNotMarkTheRoundSaved()
    {
        var memory = new ReplayMemory(5);
        memory.Add(Round(1, Ct1));

        Assert.Equal(SaveLookupStatus.Found, memory.TryFind(Ct1, 1).Status);
        Assert.Equal(SaveLookupStatus.Found, memory.TryFind(Ct1, 1).Status);
    }

    [Fact]
    public void MarkSaved_ThenTryFind_IsAlreadySaved_ForThatPlayerOnly()
    {
        var memory = new ReplayMemory(5);
        memory.Add(Round(1, Ct1, T1));

        var found = memory.TryFind(Ct1, 1);
        Assert.True(memory.MarkSaved(Ct1, found.Round!.RoundKey));

        var again = memory.TryFind(Ct1, 1);
        Assert.Equal(SaveLookupStatus.AlreadySaved, again.Status);
        Assert.Equal(1, again.Round!.RoundKey);
        Assert.Equal(SaveLookupStatus.Found, memory.TryFind(T1, 1).Status);
    }

    [Fact]
    public void RefusedEnqueue_LeavesTheRoundUnsaved_SoItCanBeRetried()
    {
        var memory = new ReplayMemory(5);
        memory.Add(Round(1, Ct1));

        var found = memory.TryFind(Ct1, 1);
        var accepted = false; // the write queue refused the command: the module does not call MarkSaved
        if (accepted)
        {
            memory.MarkSaved(Ct1, found.Round!.RoundKey);
        }

        Assert.Equal(SaveLookupStatus.Found, memory.TryFind(Ct1, 1).Status);
    }

    [Fact]
    public void MarkSaved_ARoundNoLongerInMemory_ReturnsFalse()
    {
        var memory = new ReplayMemory(1);
        memory.Add(Round(1, Ct1));
        memory.Add(Round(2, Ct1));

        Assert.False(memory.MarkSaved(Ct1, 1));
        Assert.False(memory.MarkSaved(T1, 2));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var memory = new ReplayMemory(5);
        memory.Add(Round(1, Ct1));
        memory.Clear();

        Assert.Equal(0, memory.Count);
        Assert.Equal(SaveLookupStatus.NotFound, memory.TryFind(Ct1, 1).Status);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("  ", 1)]
    [InlineData("1", 1)]
    [InlineData(" 3 ", 3)]
    [InlineData("5", 5)]
    [InlineData("0", null)]
    [InlineData("6", null)]
    [InlineData("-1", null)]
    [InlineData("+2", null)]
    [InlineData("abc", null)]
    [InlineData("2.0", null)]
    [InlineData("99999999999", null)]
    public void ParseRoundsAgo_AcceptsOneToTheCapacity(string? argument, int? expected) =>
        Assert.Equal(expected, ReplayMemory.ParseRoundsAgo(argument, 5));

    [Fact]
    public void Capacity_MustBePositive() => Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayMemory(0));
}

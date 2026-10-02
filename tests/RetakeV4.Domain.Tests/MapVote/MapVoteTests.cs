using RetakeV4.Domain.Tests.TestDoubles;
using Vote = RetakeV4.Domain.MapVote.MapVote;

namespace RetakeV4.Domain.Tests.MapVote;

public class MapVoteTests
{
    private static readonly string[] Maps = { "de_ancient", "de_mirage", "de_nuke" };

    [Fact]
    public void MostVotesWins_AndAPlayerCanChangeTheirVote()
    {
        var vote = Vote.Open(Maps).Cast(1, "de_nuke").Cast(2, "de_mirage").Cast(3, "de_mirage").Cast(3, "de_nuke");
        Assert.Equal(("de_nuke", 2), vote.Result(new FixedRandom()));
        Assert.Equal("de_nuke", vote.Choice(3));
    }

    [Fact]
    public void UnknownMap_IsIgnored()
    {
        Assert.Null(Vote.Open(Maps).Cast(1, "de_dust2").Choice(1));
    }

    [Fact]
    public void Tie_IsDrawnAmongTheLeaders()
    {
        var vote = Vote.Open(Maps).Cast(1, "de_ancient").Cast(2, "de_nuke");
        Assert.Equal(("de_nuke", 1), vote.Result(new FixedRandom(1)));
    }

    // Review Focus 1: the only voter leaves, the result is drawn from the whole list.
    [Fact]
    public void VoterLeaves_TheirVoteNoLongerCounts()
    {
        var vote = Vote.Open(Maps).Cast(1, "de_mirage").Remove(1);
        Assert.Equal(("de_nuke", 0), vote.Result(new FixedRandom(2)));
    }

    [Fact]
    public void EmptyList_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Vote.Open(Array.Empty<string>()).Result(new FixedRandom()));
    }
}

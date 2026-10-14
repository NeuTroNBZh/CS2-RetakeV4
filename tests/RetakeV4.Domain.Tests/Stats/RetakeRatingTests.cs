using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class RetakeRatingTests
{
    private static PlayerRoundStats Player() => new(1, "a", TeamSide.CT);

    [Fact]
    public void SurvivorWithOneKill() =>
        Assert.Equal(1.40, RetakeRating.For(Player() with { Kills = 1, Survived = true, Damage = 100, Kast = true }, Rules.Rating), 3);

    [Fact]
    public void DeadWithNothing_GetsTheBase() =>
        Assert.Equal(0.30, RetakeRating.For(Player(), Rules.Rating), 3);

    [Fact]
    public void Impact_AddsEntryClutchDefuseAndMultiKill()
    {
        var player = Player() with
        {
            Kills = 3, Survived = true, Damage = 250, Kast = true, EntryKill = true, ClutchVs = 2, ClutchWon = true, Defused = true,
        };
        Assert.Equal(2.3, RetakeRating.Impact(player), 3);
        Assert.Equal(3.16, RetakeRating.For(player, Rules.Rating), 3);
    }

    [Fact]
    public void Plant_AddsImpact() => Assert.Equal(0.3, RetakeRating.Impact(Player() with { Planted = true }), 3);

    [Fact]
    public void LostClutch_AddsNoImpact() => Assert.Equal(0, RetakeRating.Impact(Player() with { ClutchVs = 3 }), 3);

    [Fact]
    public void Weights_ComeFromTheRules() =>
        Assert.Equal(1.0, RetakeRating.For(Player() with { Kills = 5, Damage = 500 }, new RatingWeights(1.0, 0, 0, 0, 0, 0)), 3);
}

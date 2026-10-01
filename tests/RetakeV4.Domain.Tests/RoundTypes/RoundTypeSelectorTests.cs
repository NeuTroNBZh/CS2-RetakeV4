using RetakeV4.Domain.RoundTypes;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.RoundTypes;

public class RoundTypeSelectorTests
{
    private static readonly string[] Available = { "Pistol", "Mid", "FullBuy" };

    private static RoundTypeRules Sequence(params RoundTypeSequenceEntry[] entries) =>
        new(RoundTypeMode.Sequence, Available, entries, "FullBuy");

    private static readonly RoundTypeRules Default = Sequence(
        new("Pistol", 3), new("Mid", 3), new("FullBuy", -1));

    [Theory]
    [InlineData(0, "Pistol")]
    [InlineData(2, "Pistol")]
    [InlineData(3, "Mid")]
    [InlineData(5, "Mid")]
    [InlineData(6, "FullBuy")]
    [InlineData(50, "FullBuy")]
    public void Sequence_FollowsCountsAndOpenEndedEntry(int roundsPlayed, string expected)
    {
        Assert.Equal(expected, RoundTypeSelector.Select(Default, roundsPlayed, new FixedRandom()));
    }

    [Fact]
    public void Sequence_ExhaustedWithoutOpenEntry_KeepsLastType()
    {
        var rules = Sequence(new("Pistol", 2), new("Mid", 1));
        Assert.Equal("Mid", RoundTypeSelector.Select(rules, 3, new FixedRandom()));
        Assert.Equal("Mid", RoundTypeSelector.Select(rules, 40, new FixedRandom()));
    }

    [Fact]
    public void Sequence_EntriesAfterOpenEndedEntryAreUnreachable()
    {
        var rules = Sequence(new("Pistol", -1), new("Mid", 3));
        Assert.Equal("Pistol", RoundTypeSelector.Select(rules, 10, new FixedRandom()));
    }

    [Fact]
    public void Sequence_Empty_UsesSpecific()
    {
        Assert.Equal("FullBuy", RoundTypeSelector.Select(Sequence(), 0, new FixedRandom()));
    }

    [Fact]
    public void NegativeRoundsPlayed_IsTreatedAsZero()
    {
        Assert.Equal("Pistol", RoundTypeSelector.Select(Default, -3, new FixedRandom()));
    }

    [Fact]
    public void Random_PicksFromAvailable()
    {
        var rules = Default with { Mode = RoundTypeMode.Random };
        Assert.Equal("Mid", RoundTypeSelector.Select(rules, 0, new FixedRandom(1)));
    }

    [Fact]
    public void Random_WithNothingAvailable_UsesSpecific()
    {
        var rules = Default with { Mode = RoundTypeMode.Random, Available = Array.Empty<string>() };
        Assert.Equal("FullBuy", RoundTypeSelector.Select(rules, 0, new FixedRandom()));
    }

    [Fact]
    public void Specific_AlwaysReturnsSpecific()
    {
        var rules = Default with { Mode = RoundTypeMode.Specific, Specific = "Mid" };
        Assert.Equal("Mid", RoundTypeSelector.Select(rules, 0, new FixedRandom()));
    }

    [Fact]
    public void Step_WritesRoundTypeIntoContext()
    {
        var step = new RoundTypeStep(Default, new FixedRandom());
        var result = step.Execute(new PreparationContext(4) { RoundsPlayed = 3 });
        Assert.Equal("Mid", result.RoundType);
        Assert.Equal(PreparationOrder.RoundType, step.Order);
        Assert.Equal("round_type", step.Name);
    }
}

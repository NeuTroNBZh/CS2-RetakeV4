using RetakeV4.Domain.Loadouts;
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
    public void Step_WritesRoundTypeAndDefinitionIntoContext()
    {
        var definitions = new Dictionary<string, RoundTypeDefinition> { ["Mid"] = Definition("Mid") };
        var step = new RoundTypeStep(Default, definitions, new FixedRandom());
        var result = step.Execute(new PreparationContext(4) { RoundsPlayed = 3 });
        Assert.Equal("Mid", result.RoundType);
        Assert.Equal("Mid", result.RoundTypeDefinition?.Name);
        Assert.Equal(PreparationOrder.RoundType, step.Order);
        Assert.Equal("round_type", step.Name);
    }

    [Fact]
    public void Step_UnknownDefinition_LeavesDefinitionNull()
    {
        var step = new RoundTypeStep(Default, new Dictionary<string, RoundTypeDefinition>(), new FixedRandom());
        Assert.Null(step.Execute(new PreparationContext(1)).RoundTypeDefinition);
    }

    private static RoundTypeDefinition Definition(string name) => new(
        name, ArmorKind.Kevlar, TeamWeapons.Empty, TeamWeapons.Empty,
        new TeamDefault(null, "weapon_glock"), new TeamDefault(null, "weapon_usp_silencer"),
        new AwpSettings(false, 1, 5, 30), new DefuseKitSettings(DefuseKitMode.All, 1, 100, false), new ZeusSettings(false, 20), "Default");
}

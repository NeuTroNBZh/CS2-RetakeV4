using RetakeV4.Domain.RoundTypes;
using RetakeV4.Modules.RoundTypes;

namespace RetakeV4.Integration.Tests.Modules.RoundTypes;

public class RoundTypesConfigValidatorTests
{
    private static readonly RoundTypesConfig Defaults = new();
    private readonly RoundTypesConfigValidator _validator = new();

    private static RoundTypeDefinitionConfig Def(string name) => new() { Name = name };

    [Fact]
    public void Defaults_AreValid_AndMatchV3Sequence()
    {
        var result = _validator.Validate(Defaults, Defaults, "roundtypes.json");
        Assert.Empty(result.Issues);
        var rules = result.Config.ToRules();
        Assert.Equal(new[] { "Pistol", "Mid", "FullBuy" }, rules.Available);
        Assert.Equal(new RoundTypeSequenceEntry("FullBuy", -1), rules.Sequence[^1]);
        Assert.Equal(RoundTypeMode.Sequence, rules.Mode);
    }

    [Fact]
    public void EmptyRoundTypeList_FallsBackToDefaults()
    {
        var result = _validator.Validate(Defaults with { RoundTypes = Array.Empty<RoundTypeDefinitionConfig>() }, Defaults, "roundtypes.json");
        Assert.Equal(3, result.Config.RoundTypes.Count);
        Assert.Contains(result.Issues, i => i.Key == nameof(RoundTypesConfig.RoundTypes));
    }

    [Fact]
    public void BlankAndDuplicateNames_AreRemoved()
    {
        var config = Defaults with { RoundTypes = new[] { Def("Pistol"), Def(" "), Def("Pistol"), Def("FullBuy"), Def("Mid") } };
        var result = _validator.Validate(config, Defaults, "roundtypes.json");
        Assert.Equal(new[] { "Pistol", "FullBuy", "Mid" }, result.Config.RoundTypes.Select(r => r.Name));
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void SequenceEntries_WithUnknownTypeOrZeroCount_AreRemoved()
    {
        var config = Defaults with
        {
            Sequence = new[] { new RoundTypeSequenceEntry("Pistol", 2), new RoundTypeSequenceEntry("Eco", 3), new RoundTypeSequenceEntry("Mid", 0), new RoundTypeSequenceEntry("FullBuy", -1) },
        };
        var result = _validator.Validate(config, Defaults, "roundtypes.json");
        Assert.Equal(new[] { "Pistol", "FullBuy" }, result.Config.Sequence.Select(e => e.RoundType));
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void UnknownSpecific_FallsBackToFirstAvailable()
    {
        var result = _validator.Validate(Defaults with { Specific = "Eco" }, Defaults, "roundtypes.json");
        Assert.Equal("Pistol", result.Config.Specific);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void NullEntries_AreRemoved_InsteadOfThrowing()
    {
        var config = Defaults with
        {
            RoundTypes = new RoundTypeDefinitionConfig?[] { null, Def("Pistol"), Def("Mid"), Def("FullBuy") }!,
            Sequence = new RoundTypeSequenceEntry?[] { null, new RoundTypeSequenceEntry(null!, 2), new RoundTypeSequenceEntry("Pistol", 3) }!,
        };
        var result = _validator.Validate(config, Defaults, "roundtypes.json");
        Assert.Equal(new[] { "Pistol", "Mid", "FullBuy" }, result.Config.RoundTypes.Select(r => r.Name));
        Assert.Equal(new[] { "Pistol" }, result.Config.Sequence.Select(e => e.RoundType));
        Assert.Equal(3, result.Issues.Count);
    }

    [Fact]
    public void CountBelowMinusOne_IsRemoved()
    {
        var config = Defaults with { Sequence = new[] { new RoundTypeSequenceEntry("Pistol", -5), new RoundTypeSequenceEntry("Mid", 2) } };
        var result = _validator.Validate(config, Defaults, "roundtypes.json");
        Assert.Equal(new[] { "Mid" }, result.Config.Sequence.Select(e => e.RoundType));
    }
}

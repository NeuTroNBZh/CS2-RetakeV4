using RetakeV4.Configuration;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.RoundTypes;

namespace RetakeV4.Modules.RoundTypes;

public sealed record RoundTypesConfig : ModuleConfig
{
    public RoundTypesConfig() => Version = 2;

    public RoundTypeMode Mode { get; init; } = RoundTypeMode.Sequence;

    public IReadOnlyList<RoundTypeDefinitionConfig> RoundTypes { get; init; } = new[]
    {
        RoundTypeDefaults.Pistol(),
        RoundTypeDefaults.Mid(),
        RoundTypeDefaults.FullBuy(),
    };

    public IReadOnlyList<RoundTypeSequenceEntry> Sequence { get; init; } = new[]
    {
        new RoundTypeSequenceEntry("Pistol", 3),
        new RoundTypeSequenceEntry("Mid", 3),
        new RoundTypeSequenceEntry("FullBuy", -1),
    };

    public string Specific { get; init; } = "FullBuy";

    public RoundTypeRules ToRules() =>
        new(Mode, RoundTypes.Select(r => r.Name).ToList(), Sequence, Specific);

    public IReadOnlyDictionary<string, RoundTypeDefinition> ToDefinitions() =>
        RoundTypes.ToDictionary(r => r.Name, r => r.ToDomain(), StringComparer.Ordinal);
}

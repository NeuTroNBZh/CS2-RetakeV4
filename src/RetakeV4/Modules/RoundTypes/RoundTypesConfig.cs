using RetakeV4.Configuration;
using RetakeV4.Domain.RoundTypes;

namespace RetakeV4.Modules.RoundTypes;

public sealed record RoundTypeDefinitionConfig
{
    public string Name { get; init; } = string.Empty;
}

public sealed record RoundTypesConfig : ModuleConfig
{
    public RoundTypesConfig() => Version = 1;

    public RoundTypeMode Mode { get; init; } = RoundTypeMode.Sequence;

    public IReadOnlyList<RoundTypeDefinitionConfig> RoundTypes { get; init; } = new[]
    {
        new RoundTypeDefinitionConfig { Name = "Pistol" },
        new RoundTypeDefinitionConfig { Name = "Mid" },
        new RoundTypeDefinitionConfig { Name = "FullBuy" },
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
}

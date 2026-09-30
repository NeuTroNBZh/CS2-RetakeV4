using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.RoundTypes;

public sealed class RoundTypeStep : IPreparationStep
{
    private readonly RoundTypeRules _rules;
    private readonly IReadOnlyDictionary<string, RoundTypeDefinition> _definitions;
    private readonly IRandom _random;

    public RoundTypeStep(RoundTypeRules rules, IReadOnlyDictionary<string, RoundTypeDefinition> definitions, IRandom random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(random);
        _rules = rules;
        _definitions = definitions;
        _random = random;
    }

    public string Name => "round_type";

    public int Order => PreparationOrder.RoundType;

    public PreparationContext Execute(PreparationContext context)
    {
        var roundType = RoundTypeSelector.Select(_rules, context.RoundsPlayed, _random);
        return context with
        {
            RoundType = roundType,
            RoundTypeDefinition = _definitions.GetValueOrDefault(roundType),
        };
    }
}

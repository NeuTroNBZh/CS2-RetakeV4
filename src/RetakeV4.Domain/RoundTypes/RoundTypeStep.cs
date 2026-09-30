using RetakeV4.Domain.Common;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.RoundTypes;

public sealed class RoundTypeStep : IPreparationStep
{
    private readonly RoundTypeRules _rules;
    private readonly IRandom _random;

    public RoundTypeStep(RoundTypeRules rules, IRandom random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);
        _rules = rules;
        _random = random;
    }

    public string Name => "round_type";

    public int Order => PreparationOrder.RoundType;

    public PreparationContext Execute(PreparationContext context) =>
        context with { RoundType = RoundTypeSelector.Select(_rules, context.RoundsPlayed, _random) };
}

namespace RetakeV4.Domain.Rounds;

public sealed class DelegatePreparationStep : IPreparationStep
{
    private readonly Func<PreparationContext, PreparationContext> _run;

    public DelegatePreparationStep(string name, int order, Func<PreparationContext, PreparationContext> run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(run);
        Name = name;
        Order = order;
        _run = run;
    }

    public string Name { get; }

    public int Order { get; }

    public PreparationContext Execute(PreparationContext context) => _run(context);
}

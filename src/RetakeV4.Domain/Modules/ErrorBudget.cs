using System.Collections.Immutable;

namespace RetakeV4.Domain.Modules;

public sealed record ErrorBudget(int MaxErrorsPerRound, ImmutableDictionary<string, int> Counts, ImmutableHashSet<string> Disabled)
{
    public static ErrorBudget Create(int maxErrorsPerRound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxErrorsPerRound, 1);
        return new ErrorBudget(maxErrorsPerRound, ImmutableDictionary<string, int>.Empty, ImmutableHashSet<string>.Empty);
    }

    public bool IsDisabled(string module) => Disabled.Contains(module);

    public ErrorBudget RecordError(string module)
    {
        var count = Counts.GetValueOrDefault(module) + 1;
        var counts = Counts.SetItem(module, count);
        return count >= MaxErrorsPerRound
            ? this with { Counts = counts, Disabled = Disabled.Add(module) }
            : this with { Counts = counts };
    }

    public ErrorBudget Disable(string module) => this with { Disabled = Disabled.Add(module) };

    public ErrorBudget ResetRound() => this with { Counts = ImmutableDictionary<string, int>.Empty };
}

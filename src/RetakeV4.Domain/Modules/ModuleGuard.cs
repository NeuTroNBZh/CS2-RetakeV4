namespace RetakeV4.Domain.Modules;

public sealed record GuardFailure(string Module, string Stage, Exception Exception, bool ModuleDisabled);

public sealed class ModuleGuard
{
    private readonly Action<GuardFailure> _onFailure;
    private ErrorBudget _budget;

    public ModuleGuard(int maxErrorsPerRound, Action<GuardFailure> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onFailure);
        _budget = ErrorBudget.Create(maxErrorsPerRound);
        _onFailure = onFailure;
    }

    public bool IsDisabled(string module) => _budget.IsDisabled(module);

    public void Run(string module, string stage, Action action) =>
        Run(module, stage, () =>
        {
            action();
            return true;
        }, false);

    public T Run<T>(string module, string stage, Func<T> action, T fallback)
    {
        if (_budget.IsDisabled(module))
        {
            return fallback;
        }
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            _budget = _budget.RecordError(module);
            _onFailure(new GuardFailure(module, stage, ex, _budget.IsDisabled(module)));
            return fallback;
        }
    }

    public void Disable(string module) => _budget = _budget.Disable(module);

    public void ResetRound() => _budget = _budget.ResetRound();
}

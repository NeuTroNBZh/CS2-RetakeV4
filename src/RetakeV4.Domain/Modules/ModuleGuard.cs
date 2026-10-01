namespace RetakeV4.Domain.Modules;

public sealed record GuardFailure(string Module, string Stage, Exception Exception, bool ModuleDisabled);

public sealed record SlowRun(string Module, string Stage, TimeSpan Elapsed);

public sealed record SlowRunReporter(TimeSpan Threshold, Func<TimeSpan> Clock, Action<SlowRun> Report);

public sealed class ModuleGuard
{
    private readonly Action<GuardFailure> _onFailure;
    private readonly SlowRunReporter? _slow;
    private ErrorBudget _budget;

    public ModuleGuard(int maxErrorsPerRound, Action<GuardFailure> onFailure, SlowRunReporter? slow = null)
    {
        ArgumentNullException.ThrowIfNull(onFailure);
        _budget = ErrorBudget.Create(maxErrorsPerRound);
        _onFailure = onFailure;
        _slow = slow;
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
        var start = _slow?.Clock() ?? TimeSpan.Zero;
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
        finally
        {
            ReportIfSlow(module, stage, start);
        }
    }

    public void Disable(string module) => _budget = _budget.Disable(module);

    public void ResetRound() => _budget = _budget.ResetRound();

    private void ReportIfSlow(string module, string stage, TimeSpan start)
    {
        if (_slow is null)
        {
            return;
        }
        var elapsed = _slow.Clock() - start;
        if (elapsed >= _slow.Threshold)
        {
            _slow.Report(new SlowRun(module, stage, elapsed));
        }
    }
}

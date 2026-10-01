using System.Collections.Immutable;
using RetakeV4.Domain.Modules;

namespace RetakeV4.Domain.Rounds;

public sealed class PreparationPipeline
{
    private readonly ModuleGuard _guard;
    private ImmutableList<Registration> _registrations = ImmutableList<Registration>.Empty;
    private long _sequence;

    public PreparationPipeline(ModuleGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _guard = guard;
    }

    public IDisposable Register(string module, IPreparationStep step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(step);
        var registration = new Registration(module, step, _sequence++);
        _registrations = _registrations.Add(registration);
        return new Unregister(() => _registrations = _registrations.Remove(registration));
    }

    public PreparationContext Execute(PreparationContext initial)
    {
        var ordered = _registrations.OrderBy(r => r.Step.Order).ThenBy(r => r.Sequence);
        var context = initial;
        foreach (var registration in ordered)
        {
            var input = context;
            context = _guard.Run(registration.Module, $"prepare:{registration.Step.Name}", () => registration.Step.Execute(input), input);
        }
        return context;
    }

    private sealed record Registration(string Module, IPreparationStep Step, long Sequence);

    private sealed class Unregister(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

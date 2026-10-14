using RetakeV4.Domain.Events;

namespace RetakeV4.Domain.Stats;

// Repeated server events (errors, slow handlers, long frames, commands, chat) are counted in memory and written as one
// row per (type, module, value) at each flush, so that a flood never fills the bounded write queue. Game thread only.
public sealed class ServerEventAggregator
{
    public const float FlushSeconds = 60f;

    // Distinct keys kept between two flushes; new keys beyond it are dropped (existing keys keep counting).
    public const int MaxKeys = 500;

    private readonly Dictionary<(string Type, string? Module, string? Value), (int Occurrences, int? MaxMs)> _pending = new();

    public int Pending => _pending.Count;

    public int DroppedKeys { get; private set; }

    public void Add(string type, string? module, string? value, int? ms = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var key = (type, module, value);
        if (_pending.TryGetValue(key, out var current))
        {
            _pending[key] = (current.Occurrences + 1, Max(current.MaxMs, ms));
            return;
        }
        if (_pending.Count >= MaxKeys)
        {
            DroppedKeys++;
            return;
        }
        _pending[key] = (1, ms);
    }

    public void Add(ModuleFailureReported failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Add(ServerEventTypes.ModuleError, failure.Module, failure.Stage);
        if (failure.ModuleDisabled)
        {
            Add(ServerEventTypes.ModuleDisabled, failure.Module, failure.Stage);
        }
    }

    public void Add(SlowHandlerReported slow)
    {
        ArgumentNullException.ThrowIfNull(slow);
        Add(ServerEventTypes.SlowHandler, slow.Module, slow.Stage, slow.ElapsedMs);
    }

    public IReadOnlyList<ServerEvent> Flush(DateTimeOffset at)
    {
        var events = _pending
            .OrderBy(p => p.Key.Type, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Module, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Value, StringComparer.Ordinal)
            .Select(p => new ServerEvent(at, p.Key.Type, p.Key.Module, p.Key.Value, p.Value.Occurrences, p.Value.MaxMs))
            .ToList();
        _pending.Clear();
        return events;
    }

    private static int? Max(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}

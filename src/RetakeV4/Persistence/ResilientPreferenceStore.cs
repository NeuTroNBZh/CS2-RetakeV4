using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class ResilientPreferenceStore : IPreferenceRepository
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly IPreferenceRepository _inner;
    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private TimeSpan _backoff = InitialBackoff;

    public ResilientPreferenceStore(IPreferenceRepository inner, ILogger logger, Func<DateTimeOffset> clock)
    {
        _inner = inner;
        _logger = logger;
        _clock = clock;
    }

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _clock() >= _retryAt;
            }
        }
    }

    public async Task<IReadOnlyList<StoredPreference>> LoadAsync(ulong steamId, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return Array.Empty<StoredPreference>();
        }
        try
        {
            var result = await _inner.LoadAsync(steamId, ct).ConfigureAwait(false);
            MarkHealthy();
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkFailed(ex, "load");
            return Array.Empty<StoredPreference>();
        }
    }

    public async Task UpsertAsync(StoredPreference preference, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return;
        }
        try
        {
            await _inner.UpsertAsync(preference, ct).ConfigureAwait(false);
            MarkHealthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkFailed(ex, "save");
        }
    }

    public Task ImportAsync(IReadOnlyList<StoredPreference> preferences, CancellationToken ct) =>
        _inner.ImportAsync(preferences, ct);

    private void MarkHealthy()
    {
        lock (_gate)
        {
            _backoff = InitialBackoff;
        }
    }

    private void MarkFailed(Exception ex, string operation)
    {
        TimeSpan wait;
        lock (_gate)
        {
            wait = _backoff;
            _retryAt = _clock() + wait;
            _backoff = TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxBackoff.Ticks));
        }
        _logger.LogWarning(ex, "Preference database unavailable ({Operation}); using defaults, retrying in {Seconds}s", operation, wait.TotalSeconds);
    }
}

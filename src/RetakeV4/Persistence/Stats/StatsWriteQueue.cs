using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace RetakeV4.Persistence.Stats;

// Single background writer. Bounded: when full, the oldest pending command is dropped. A failed write is retried with
// backoff and keeps its place; disposal drains for a while, then abandons what is left.
public sealed class StatsWriteQueue : IAsyncDisposable
{
    public const int PurgeBatchSize = 5000;
    public const int DefaultMaxAttempts = 10;

    private readonly Channel<StatsCommand> _channel;
    private readonly IStatsRepository _repository;
    private readonly ILogger _logger;
    private readonly TimeSpan _drainTimeout;
    private readonly Func<int, TimeSpan> _backoff;
    private readonly int _maxAttempts;
    private readonly Action<Exception>? _initializationFailed;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<bool> _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<ulong, long> _sessions = new();
    private readonly Task _worker;
    private long? _mapId;
    private string? _mapName;
    private bool _failed;
    private int _dropped;
    private int _abandoned;
    private int _disposed;
    private int _failedCommands;

    public StatsWriteQueue(
        IStatsRepository repository, ILogger logger, int capacity, TimeSpan drainTimeout,
        Action<Exception>? initializationFailed = null, Func<int, TimeSpan>? backoff = null,
        int maxAttempts = DefaultMaxAttempts)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        if (drainTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(drainTimeout), "The drain timeout must be finite and not negative");
        }
        _repository = repository;
        _logger = logger;
        _drainTimeout = drainTimeout;
        _initializationFailed = initializationFailed;
        _backoff = backoff ?? DefaultBackoff;
        _maxAttempts = maxAttempts;
        _channel = Channel.CreateBounded<StatsCommand>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            OnDropped);
        _worker = Task.Run(RunAsync);
    }

    // Readers (ELO loading) wait for this before their first query, so that they never run before the schema exists.
    public Task<bool> Initialized => _initialized.Task;

    public int Dropped => Volatile.Read(ref _dropped);

    public int Abandoned => Volatile.Read(ref _abandoned);

    // Commands dropped after maxAttempts failed attempts (a poison command must not stall the queue).
    public int Failed => Volatile.Read(ref _failedCommands);

    public static TimeSpan DefaultBackoff(int attempt) => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Max(0, attempt - 1))));

    public void Enqueue(StatsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_channel.Writer.TryWrite(command))
        {
            Interlocked.Increment(ref _abandoned);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        _channel.Writer.TryComplete();
        if (await Task.WhenAny(_worker, Task.Delay(_drainTimeout)).ConfigureAwait(false) != _worker)
        {
            // Not awaited: cancellation callbacks (e.g. a driver opening a connection to kill a query) must not extend the bounded wait.
            _ = Task.Run(() =>
            {
                try
                {
                    _stop.Cancel();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stats write queue cancellation callback threw");
                }
            });
            await Task.WhenAny(_worker, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        }
        if (Abandoned > 0)
        {
            _logger.LogWarning("Stats write queue stopped: {Count} pending writes were abandoned", Abandoned);
        }
    }

    private void OnDropped(StatsCommand command)
    {
        Interlocked.Increment(ref _dropped);
        _logger.LogWarning("Stats write queue is full: dropped the oldest pending {Command}", command.GetType().Name);
    }

    private async Task RunAsync()
    {
        try
        {
            await _repository.InitializeAsync(_stop.Token).ConfigureAwait(false);
            _initialized.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _failed = true;
            try
            {
                if (!_stop.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Stats database could not be initialized; stats are not recorded");
                    _initializationFailed?.Invoke(ex);
                }
            }
            catch (Exception handlerEx)
            {
                _logger.LogError(handlerEx, "Stats initialization-failure handler threw");
            }
            finally
            {
                _initialized.TrySetResult(false);
            }
        }
        await foreach (var command in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_failed)
            {
                Interlocked.Increment(ref _abandoned);
            }
            else
            {
                await ApplyWithRetryAsync(command).ConfigureAwait(false);
            }
        }
    }

    private async Task ApplyWithRetryAsync(StatsCommand command)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (_stop.IsCancellationRequested)
            {
                Interlocked.Increment(ref _abandoned);
                return;
            }
            try
            {
                await ApplyAsync(command, _stop.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                Interlocked.Increment(ref _abandoned);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= _maxAttempts)
                {
                    Interlocked.Increment(ref _failedCommands);
                    _logger.LogError(ex, "Stats write gave up after {Attempts} attempts: {Command} dropped", attempt, command.GetType().Name);
                    return;
                }
                _logger.LogWarning(ex, "Stats write failed (attempt {Attempt}): {Command}; retrying", attempt, command.GetType().Name);
                try
                {
                    await Task.Delay(_backoff(attempt), _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _abandoned);
                    return;
                }
            }
        }
    }

    private async Task ApplyAsync(StatsCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case OpenMapCommand open:
                _mapId = await _repository.OpenMapAsync(open.MapName, open.At, ct).ConfigureAwait(false);
                _mapName = open.MapName;
                _sessions.Clear();
                break;
            case CloseMapCommand close:
                if (_mapId is { } closing)
                {
                    await _repository.CloseMapAsync(closing, close.At, ct).ConfigureAwait(false);
                }
                break;
            case OpenSessionCommand session:
                var sessionMap = await EnsureMapAsync(null, session.At, ct).ConfigureAwait(false);
                _sessions[session.SteamId] = await _repository.OpenSessionAsync(sessionMap, session.SteamId, session.Name, session.At, ct).ConfigureAwait(false);
                break;
            case CloseSessionCommand session:
                var closeMap = await EnsureMapAsync(null, session.At, ct).ConfigureAwait(false);
                long? sessionId = _sessions.TryGetValue(session.SteamId, out var id) ? id : null;
                await _repository.CloseSessionAsync(sessionId, closeMap, session.SteamId, session.At, session.Reason, session.Playtime, ct).ConfigureAwait(false);
                _sessions.Remove(session.SteamId);
                break;
            case WriteRoundCommand write:
                var context = write.Round.Record.Context;
                var roundMap = await EnsureMapAsync(context.MapName, context.StartedAt, ct).ConfigureAwait(false);
                await _repository.WriteRoundAsync(roundMap, write.Round, ct).ConfigureAwait(false);
                break;
            case PurgeCommand purge:
                await _repository.PurgeRawAsync(purge.OlderThan, PurgeBatchSize, ct).ConfigureAwait(false);
                break;
        }
    }

    // The map opening may have been dropped (full queue) or may have failed: open it lazily when needed.
    private async Task<long> EnsureMapAsync(string? mapName, DateTimeOffset at, CancellationToken ct)
    {
        if (_mapId is { } id && (mapName is null || mapName == _mapName))
        {
            return id;
        }
        var name = mapName ?? _mapName ?? "unknown";
        _mapId = await _repository.OpenMapAsync(name, at, ct).ConfigureAwait(false);
        _mapName = name;
        return _mapId.Value;
    }
}

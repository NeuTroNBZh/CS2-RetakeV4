using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace RetakeV4.Persistence.Stats;

// Single background writer. Bounded: when full, the oldest pending command is dropped. Telemetry yields first: it is skipped
// (never queued) once half of the capacity is pending, so that it never pushes rounds out. Initialization (schema) is retried
// with backoff until it succeeds or the queue stops; commands keep queuing meanwhile. A failed write is retried with
// backoff and keeps its place; disposal drains for a while, then abandons what is left.
public sealed class StatsWriteQueue : IAsyncDisposable
{
    public const int PurgeBatchSize = 5000;
    public const int DefaultMaxAttempts = 10;

    // Replay commands waiting at most (each holds a round of frame text).
    public const int MaxPendingReplays = 4;

    // Round ids remembered by RoundKey: a replay follows its round within ReplayMemoryRounds (10 at most) rounds.
    public const int RememberedRounds = 32;

    private readonly Channel<StatsCommand> _channel;
    private readonly IStatsRepository _repository;
    private readonly ILogger _logger;
    private readonly TimeSpan _drainTimeout;
    private readonly Func<int, TimeSpan> _backoff;
    private readonly int _maxAttempts;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<bool> _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<ulong, long> _sessions = new();
    private readonly Dictionary<long, long> _roundIds = new();
    private readonly Queue<long> _roundKeys = new();
    private readonly Task _worker;
    private readonly int _telemetryLimit;
    private long? _mapId;
    private string? _mapName;
    private int _dropped;
    private int _abandoned;
    private int _disposed;
    private int _failedCommands;
    private int _telemetrySkipped;
    private int _skippingTelemetry;
    private int _pendingReplays;
    private int _replaysSkipped;
    private int _skippingReplays;

    public StatsWriteQueue(
        IStatsRepository repository, ILogger logger, int capacity, TimeSpan drainTimeout,
        Func<int, TimeSpan>? backoff = null, int maxAttempts = DefaultMaxAttempts)
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
        _backoff = backoff ?? DefaultBackoff;
        _maxAttempts = maxAttempts;
        _telemetryLimit = Math.Max(1, capacity / 2);
        _channel = Channel.CreateBounded<StatsCommand>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            OnDropped);
        _worker = Task.Run(RunAsync);
    }

    // Readers (ELO loading) wait for this before their first query, so that they never run before the schema exists.
    // True once the schema is ready; false only when the queue stopped before that.
    public Task<bool> Initialized => _initialized.Task;

    public int Dropped => Volatile.Read(ref _dropped);

    public int Abandoned => Volatile.Read(ref _abandoned);

    // Commands dropped after maxAttempts failed attempts (a poison command must not stall the queue).
    public int Failed => Volatile.Read(ref _failedCommands);

    // Telemetry commands not queued because half of the capacity was pending.
    public int TelemetrySkipped => Volatile.Read(ref _telemetrySkipped);

    public int PendingReplays => Volatile.Read(ref _pendingReplays);

    // Replay commands not queued (MaxPendingReplays waiting, or half of the capacity pending).
    public int ReplaysSkipped => Volatile.Read(ref _replaysSkipped);

    public static TimeSpan DefaultBackoff(int attempt) => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Max(0, attempt - 1))));

    public void Enqueue(StatsCommand command) => TryEnqueue(command);

    // False when a replay is refused (MaxPendingReplays pending, the queue half full, or the queue already stopped); other
    // commands are always accepted here (after disposal they only count as abandoned).
    public bool TryEnqueue(StatsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is ServerEventsCommand { Events.Count: 0 } || (command is TelemetryCommand && !AcceptsTelemetry(command)))
        {
            return true;
        }
        if (command is WriteReplayCommand && !AcceptsReplay())
        {
            return false;
        }
        if (!_channel.Writer.TryWrite(command))
        {
            Release(command);
            Interlocked.Increment(ref _abandoned);
            return command is not WriteReplayCommand;
        }
        return true;
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

    // One warning per burst: the flag is cleared by the next telemetry command that fits.
    private bool AcceptsTelemetry(StatsCommand command)
    {
        if (_channel.Reader.Count < _telemetryLimit)
        {
            Interlocked.Exchange(ref _skippingTelemetry, 0);
            return true;
        }
        Interlocked.Increment(ref _telemetrySkipped);
        if (Interlocked.Exchange(ref _skippingTelemetry, 1) == 0)
        {
            _logger.LogWarning("Stats write queue is backed up: telemetry ({Command}) is skipped until it drains", command.GetType().Name);
        }
        return false;
    }

    // A replay waits only while fewer than MaxPendingReplays do and less than half of the capacity is pending, so that a
    // database outage neither grows the memory nor pushes rounds out. One warning per burst.
    private bool AcceptsReplay()
    {
        if (_channel.Reader.Count < _telemetryLimit)
        {
            if (Interlocked.Increment(ref _pendingReplays) <= MaxPendingReplays)
            {
                Interlocked.Exchange(ref _skippingReplays, 0);
                return true;
            }
            Interlocked.Decrement(ref _pendingReplays);
        }
        Interlocked.Increment(ref _replaysSkipped);
        if (Interlocked.Exchange(ref _skippingReplays, 1) == 0)
        {
            _logger.LogWarning("Stats write queue is backed up: replays are skipped until it drains");
        }
        return false;
    }

    private void Release(StatsCommand command)
    {
        if (command is WriteReplayCommand)
        {
            Interlocked.Decrement(ref _pendingReplays);
        }
    }

    private void OnDropped(StatsCommand command)
    {
        Release(command);
        Interlocked.Increment(ref _dropped);
        _logger.LogWarning("Stats write queue is full: dropped the oldest pending {Command}", command.GetType().Name);
    }

    private async Task RunAsync()
    {
        var initialized = await InitializeWithRetryAsync().ConfigureAwait(false);
        _initialized.TrySetResult(initialized);
        await foreach (var command in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (initialized)
            {
                await ApplyWithRetryAsync(command).ConfigureAwait(false);
            }
            else
            {
                Interlocked.Increment(ref _abandoned);
            }
            Release(command);
        }
    }

    // A database unreachable at startup (or a concurrent schema migration by another server) must not switch stats off
    // for the whole uptime: retry until it works or the queue is stopped.
    private async Task<bool> InitializeWithRetryAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            if (_stop.IsCancellationRequested)
            {
                return false;
            }
            try
            {
                await _repository.InitializeAsync(_stop.Token).ConfigureAwait(false);
                if (attempt > 1)
                {
                    _logger.LogInformation("Stats database initialized after {Attempts} attempts", attempt);
                }
                return true;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (attempt == 1)
                {
                    _logger.LogError(ex, "Stats database could not be initialized; stats are queued and initialization is retried");
                }
                else
                {
                    _logger.LogWarning(ex, "Stats database initialization failed again (attempt {Attempt}); retrying", attempt);
                }
                try
                {
                    await Task.Delay(_backoff(attempt), _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
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
                await _repository.CloseSessionAsync(sessionId, closeMap, session.SteamId, session.At, session.Reason, session.Playtime, session.Activity, ct).ConfigureAwait(false);
                _sessions.Remove(session.SteamId);
                break;
            case WriteRoundCommand write:
                var context = write.Round.Record.Context;
                var roundMap = await EnsureMapAsync(context.MapName, context.StartedAt, ct).ConfigureAwait(false);
                var roundId = await _repository.WriteRoundAsync(roundMap, write.Round, ct).ConfigureAwait(false);
                if (write.RoundKey != 0)
                {
                    RememberRound(write.RoundKey, roundId);
                }
                break;
            case WriteReplayCommand replay:
                if (_roundIds.TryGetValue(replay.RoundKey, out var replayRound))
                {
                    await _repository.WriteReplayAsync(replayRound, replay.Replay, replay.Claims, replay.Caps, replay.At, ct).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogInformation("Stats: the round of a replay was not written (dropped or failed); the replay is skipped");
                }
                break;
            case PlayerCountryCommand country:
                long? countrySession = _sessions.TryGetValue(country.SteamId, out var countryId) ? countryId : null;
                await _repository.SetCountryAsync(countrySession, country.SteamId, country.Country, ct).ConfigureAwait(false);
                break;
            case PresenceCommand presence:
                await _repository.WritePresenceAsync(presence.Snapshot, ct).ConfigureAwait(false);
                break;
            case ServerEventsCommand serverEvents:
                await _repository.WriteServerEventsAsync(serverEvents.Events, ct).ConfigureAwait(false);
                break;
            case PurgeCommand purge:
                await _repository.PurgeRawAsync(purge.OlderThan, PurgeBatchSize, ct).ConfigureAwait(false);
                break;
        }
    }

    private void RememberRound(long key, long roundId)
    {
        if (!_roundIds.TryAdd(key, roundId))
        {
            return;
        }
        _roundKeys.Enqueue(key);
        if (_roundKeys.Count > RememberedRounds)
        {
            _roundIds.Remove(_roundKeys.Dequeue());
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

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Persistence;

public sealed class PreferenceWriteQueue : IAsyncDisposable
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<StoredPreference> _channel = Channel.CreateUnbounded<StoredPreference>(new UnboundedChannelOptions { SingleReader = true });
    private readonly IPreferenceRepository _store;
    private readonly ILogger _logger;
    private readonly Task _worker;

    public PreferenceWriteQueue(IPreferenceRepository store, ILogger logger)
    {
        _store = store;
        _logger = logger;
        _worker = Task.Run(RunAsync);
    }

    public void Enqueue(StoredPreference preference) => _channel.Writer.TryWrite(preference);

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await Task.WhenAny(_worker, Task.Delay(DrainTimeout)).ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        await foreach (var preference in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await _store.UpsertAsync(preference, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save a preference for {SteamId}", preference.Key.SteamId);
            }
        }
    }
}

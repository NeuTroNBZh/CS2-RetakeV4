using CounterStrikeSharp.API.Core.Capabilities;

namespace RetakeV4.Contracts;

// CounterStrikeSharp keeps every capability provider forever and Get() always calls the first one, so the provider is
// registered once, here in the shared assembly (loaded once per server): plugin reloads only swap the published instance.
internal static class RetakeApiHost
{
    private static readonly object Gate = new();
    private static IRetakeApi? _current;
    private static bool _registered;

    internal static void Publish(IRetakeApi? api)
    {
        lock (Gate)
        {
            _current = api;
            if (_registered)
            {
                return;
            }
            Capabilities.RegisterPluginCapability(RetakeApi.Capability, () => _current);
            _registered = true;
        }
    }
}

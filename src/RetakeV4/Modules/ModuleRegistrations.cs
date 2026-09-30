using Microsoft.Extensions.Logging;

namespace RetakeV4.Modules;

public sealed class ModuleRegistrations : IDisposable
{
    private readonly string _module;
    private readonly ILogger _logger;
    private readonly List<Action> _undo = new();

    public ModuleRegistrations(string module, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(logger);
        _module = module;
        _logger = logger;
    }

    public int Count => _undo.Count;

    public void Track(Action undo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        _undo.Add(undo);
    }

    public void Dispose()
    {
        for (var i = _undo.Count - 1; i >= 0; i--)
        {
            try
            {
                _undo[i]();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {Module}: failed to release a registration", _module);
            }
        }
        _undo.Clear();
    }
}

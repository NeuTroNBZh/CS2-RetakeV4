using RetakeV4.Modules;

namespace RetakeV4.Integration.Tests.Modules;

public class ModuleRegistrationsTests
{
    private readonly ListLogger _logger = new();

    [Fact]
    public void Dispose_UndoesInReverseOrder()
    {
        var journal = new List<string>();
        var registrations = new ModuleRegistrations("Core", _logger);
        registrations.Track(() => journal.Add("first"));
        registrations.Track(() => journal.Add("second"));
        registrations.Dispose();
        Assert.Equal(new[] { "second", "first" }, journal);
    }

    [Fact]
    public void Dispose_IsolatesFailures_AndLogsThem()
    {
        var journal = new List<string>();
        var registrations = new ModuleRegistrations("Core", _logger);
        registrations.Track(() => journal.Add("first"));
        registrations.Track(() => throw new InvalidOperationException("boom"));
        registrations.Dispose();
        Assert.Equal(new[] { "first" }, journal);
        Assert.Single(_logger.Entries);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var calls = 0;
        var registrations = new ModuleRegistrations("Core", _logger);
        registrations.Track(() => calls++);
        registrations.Dispose();
        registrations.Dispose();
        Assert.Equal(1, calls);
        Assert.Equal(0, registrations.Count);
    }
}

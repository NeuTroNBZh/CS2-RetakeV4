using Microsoft.Extensions.Logging;
using RetakeV4.Modules.Stats;

namespace RetakeV4.Integration.Tests.Modules.Stats;

public sealed class MmdbCountryLookupTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void MissingFile_GivesNoLookup_AndOneInformationLine()
    {
        var logger = new ListLogger();
        Assert.Null(MmdbCountryLookup.Open(_dir.File("geo/dbip-country-lite.mmdb"), logger));
        Assert.Equal(LogLevel.Information, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public void UnreadableFile_GivesNoLookup_AndOneWarning()
    {
        var path = _dir.File("geo/broken.mmdb");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a MaxMind database");
        var logger = new ListLogger();
        Assert.Null(MmdbCountryLookup.Open(path, logger));
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }
}

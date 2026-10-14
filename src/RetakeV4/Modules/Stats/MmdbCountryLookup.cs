using System.Net;
using MaxMind.Db;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Modules.Stats;

// DB-IP Lite country database (https://db-ip.com, CC BY 4.0), loaded once in memory (no file lock). Thread-safe.
internal sealed class MmdbCountryLookup : ICountryLookup, IDisposable
{
    private readonly Reader _reader;

    private MmdbCountryLookup(Reader reader) => _reader = reader;

    // Null, with one log line, when the file is missing or unreadable: countries then stay NULL.
    public static MmdbCountryLookup? Open(string path, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (!File.Exists(path))
        {
            logger.LogInformation("Stats: no GeoIP database at {Path}; player countries are not recorded", path);
            return null;
        }
        try
        {
            return new MmdbCountryLookup(new Reader(path, FileAccessMode.Memory));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stats: the GeoIP database {Path} could not be read; player countries are not recorded", path);
            return null;
        }
    }

    public string? Country(IPAddress address)
    {
        var data = _reader.Find<Dictionary<string, object>>(address);
        return data is not null
            && data.TryGetValue("country", out var country) && country is Dictionary<string, object> fields
            && fields.TryGetValue("iso_code", out var code)
            ? code as string
            : null;
    }

    public void Dispose() => _reader.Dispose();
}

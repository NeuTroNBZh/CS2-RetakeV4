using RetakeV4.Domain.Stats;
using RetakeV4.Persistence.Stats;
using static RetakeV4.Integration.Tests.Persistence.Stats.StatsSamples;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

public class MySqlStatsRepositoryTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RETAKEV4_MYSQL_TEST");

    [Fact]
    public async Task RoundTrip_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repository = StatsRepositories.MySql(ConnectionString, new StatsWriteOptions($"test-{Guid.NewGuid():N}"[..20], 1000, 100));
        await repository.InitializeAsync(CancellationToken.None);
        await repository.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var before = (await repository.LoadEloAsync(new[] { Ct }, CancellationToken.None)).GetValueOrDefault(Ct, 1000);
        var mapId = await repository.OpenMapAsync("de_mirage", now, CancellationToken.None);
        await repository.WriteRoundAsync(mapId, Round(now, ctElo: before), CancellationToken.None);
        var session = await repository.OpenSessionAsync(mapId, Ct, "ct", now, CancellationToken.None);
        await repository.CloseSessionAsync(session, mapId, Ct, now.AddMinutes(1), 0, new Playtime(60, 0, 0, 0), CancellationToken.None);
        await repository.CloseMapAsync(mapId, now.AddMinutes(2), CancellationToken.None);
        var after = (await repository.LoadEloAsync(new[] { Ct }, CancellationToken.None))[Ct];
        Assert.NotEqual(before, after);
        await repository.PurgeRawAsync(now.AddDays(-3650), 500, CancellationToken.None);
    }
}

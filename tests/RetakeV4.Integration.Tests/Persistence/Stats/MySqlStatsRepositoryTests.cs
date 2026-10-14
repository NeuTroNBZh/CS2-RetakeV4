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
        await repository.WriteRoundAsync(mapId, RoundV2(now), CancellationToken.None);
        var session = await repository.OpenSessionAsync(mapId, Ct, "ct", now, CancellationToken.None);
        await repository.SetCountryAsync(session, Ct, "FR", CancellationToken.None);
        await repository.CloseSessionAsync(session, mapId, Ct, now.AddMinutes(1), 0, new Playtime(60, 0, 0, 0) { SecondsVip = 5 },
            new SessionActivity(40, 60), CancellationToken.None);
        await repository.WritePresenceAsync(new PresenceSnapshot(now, "de_mirage", 2, 0, 0), CancellationToken.None);
        await repository.WriteServerEventAsync(new ServerEvent(now, ServerEventTypes.Chat, null, "say", 4), CancellationToken.None);
        await repository.CloseMapAsync(mapId, now.AddMinutes(2), CancellationToken.None);
        var after = (await repository.LoadEloAsync(new[] { Ct }, CancellationToken.None))[Ct];
        Assert.NotEqual(before, after);
        await repository.PurgeRawAsync(now.AddDays(-3650), 500, CancellationToken.None);
    }

    // Several servers starting together on one database: the migration lock and the replayable ALTERs keep it safe.
    [Fact]
    public async Task ConcurrentInitialize_WhenAMySqlServerIsConfigured()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }
        var repositories = Enumerable.Range(0, 4)
            .Select(i => StatsRepositories.MySql(ConnectionString, new StatsWriteOptions($"conc-{i}", 1000, 100)))
            .ToList();
        await Task.WhenAll(repositories.Select(r => Task.Run(() => r.InitializeAsync(CancellationToken.None))));
    }
}

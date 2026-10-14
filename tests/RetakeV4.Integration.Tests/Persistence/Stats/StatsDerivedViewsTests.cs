using Microsoft.Data.Sqlite;
using RetakeV4.Persistence;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

public sealed class StatsDerivedViewsTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private async Task<SqliteConnection> Migrated()
    {
        var connection = new SqliteConnection($"Data Source={_dir.File("stats.db")}");
        await connection.OpenAsync();
        await SqlMigrator.ApplyAsync(connection, StatsSchema.Migrations(StatsDialect.Sqlite), CancellationToken.None, StatsSchema.VersionTable);
        return connection;
    }

    [Fact]
    public async Task SideBalance_CountsWinsPerServerMapAndSite()
    {
        await using var connection = await Migrated();
        await DerivedViewScenarios.SideBalance(new ViewSeed(connection, StatsDialect.Sqlite));
    }

    [Fact]
    public async Task RetakeOdds_GroupsByPlayerCountsAndThePlantSituation()
    {
        await using var connection = await Migrated();
        await DerivedViewScenarios.RetakeOdds(new ViewSeed(connection, StatsDialect.Sqlite));
    }

    [Fact]
    public async Task WeaponMeta_AddsUpEveryPlayer_AndThePrimaryWeaponRounds()
    {
        await using var connection = await Migrated();
        await DerivedViewScenarios.WeaponMeta(new ViewSeed(connection, StatsDialect.Sqlite), freshDatabase: true);
    }

    [Fact]
    public async Task Rivals_CountDuelsBetweenHumans_OnceEachDeath()
    {
        await using var connection = await Migrated();
        await DerivedViewScenarios.Rivals(new ViewSeed(connection, StatsDialect.Sqlite));
    }

    [Fact]
    public async Task Duos_CountRoundsTogether_AndTradesWithinFiveSeconds()
    {
        await using var connection = await Migrated();
        await DerivedViewScenarios.Duos(new ViewSeed(connection, StatsDialect.Sqlite));
    }

    [Fact]
    public async Task ServerActivity_GroupsPresenceRoundsAndSessions_PerUtcHour()
    {
        await using var connection = await Migrated();
        await DerivedViewScenarios.ServerActivity(new ViewSeed(connection, StatsDialect.Sqlite));
    }
}

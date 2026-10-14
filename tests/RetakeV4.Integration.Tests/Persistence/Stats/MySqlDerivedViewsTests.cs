using MySqlConnector;
using RetakeV4.Persistence;
using RetakeV4.Persistence.Stats;

namespace RetakeV4.Integration.Tests.Persistence.Stats;

// The derived-view scenarios on MySQL 8 / MariaDB 11.4 (default sql_mode, so ONLY_FULL_GROUP_BY on MySQL). Same collection
// as StatsSchemaTests, whose reapply test drops and recreates every view: never in parallel on the shared database.
[Collection(StatsMySqlCollection.Name)]
public class MySqlDerivedViewsTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RETAKEV4_MYSQL_TEST");

    private static async Task<MySqlConnection?> Migrated()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return null;
        }
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await SqlMigrator.ApplyAsync(connection, StatsSchema.Migrations(StatsDialect.MySql), CancellationToken.None, StatsSchema.VersionTable);
        return connection;
    }

    [Fact]
    public async Task SideBalance_WhenAMySqlServerIsConfigured()
    {
        await using var connection = await Migrated();
        if (connection is null)
        {
            return;
        }
        await DerivedViewScenarios.SideBalance(new ViewSeed(connection, StatsDialect.MySql));
    }

    [Fact]
    public async Task RetakeOdds_WhenAMySqlServerIsConfigured()
    {
        await using var connection = await Migrated();
        if (connection is null)
        {
            return;
        }
        await DerivedViewScenarios.RetakeOdds(new ViewSeed(connection, StatsDialect.MySql));
    }

    [Fact]
    public async Task WeaponMeta_WhenAMySqlServerIsConfigured()
    {
        await using var connection = await Migrated();
        if (connection is null)
        {
            return;
        }
        await DerivedViewScenarios.WeaponMeta(new ViewSeed(connection, StatsDialect.MySql), freshDatabase: false);
    }

    [Fact]
    public async Task Rivals_WhenAMySqlServerIsConfigured()
    {
        await using var connection = await Migrated();
        if (connection is null)
        {
            return;
        }
        await DerivedViewScenarios.Rivals(new ViewSeed(connection, StatsDialect.MySql));
    }

    [Fact]
    public async Task Duos_WhenAMySqlServerIsConfigured()
    {
        await using var connection = await Migrated();
        if (connection is null)
        {
            return;
        }
        await DerivedViewScenarios.Duos(new ViewSeed(connection, StatsDialect.MySql));
    }

    [Fact]
    public async Task ServerActivity_WhenAMySqlServerIsConfigured()
    {
        await using var connection = await Migrated();
        if (connection is null)
        {
            return;
        }
        await DerivedViewScenarios.ServerActivity(new ViewSeed(connection, StatsDialect.MySql));
    }
}

// Test classes that use the shared MySQL stats database while dropping or recreating its views.
internal static class StatsMySqlCollection
{
    internal const string Name = "Stats MySQL views";
}

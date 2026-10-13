using RetakeV4.Configuration;
using RetakeV4.Modules.Allocation;
using RetakeV4.Modules.Stats;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Configuration;

public class DatabaseConsistencyCheckTests
{
    private static readonly DatabaseConfig MySql = new() { Type = DatabaseType.MySql, MySqlConnectionString = "Server=db;Database=retake", ServerKey = "eu-1" };

    [Fact]
    public void Defaults_AreConsistent() => Assert.Empty(DatabaseConsistencyCheck.Check(new AllocationConfig(), new StatsConfig()));

    [Fact]
    public void SameMySqlDatabase_IsConsistent() =>
        Assert.Empty(DatabaseConsistencyCheck.Check(new AllocationConfig { Database = MySql }, new StatsConfig { Database = MySql }));

    [Theory]
    [InlineData("Server=other;Database=retake", "eu-1")]
    [InlineData("Server=db;Database=retake", "eu-2")]
    public void DifferentDatabaseOrServerKey_IsReported(string connection, string serverKey)
    {
        var stats = new StatsConfig { Database = MySql with { MySqlConnectionString = connection, ServerKey = serverKey } };
        var problem = Assert.Single(DatabaseConsistencyCheck.Check(new AllocationConfig { Database = MySql }, stats));
        Assert.Contains("stats.json", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledStats_OrNoAllocationDatabase_IsNotChecked()
    {
        Assert.Empty(DatabaseConsistencyCheck.Check(new AllocationConfig { Database = MySql }, new StatsConfig { Enabled = false }));
        Assert.Empty(DatabaseConsistencyCheck.Check(new AllocationConfig { Database = new DatabaseConfig { Type = DatabaseType.None } }, new StatsConfig()));
    }
}

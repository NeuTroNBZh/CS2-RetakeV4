using RetakeV4.Domain.Loadouts;
using RetakeV4.Modules.Allocation;

namespace RetakeV4.Integration.Tests.Modules.Allocation;

public class AllocationConfigValidatorTests
{
    private static readonly AllocationConfig Defaults = new();
    private readonly AllocationConfigValidator _validator = new();

    private AllocationConfig Validate(DatabaseConfig database, out int issues)
    {
        var result = _validator.Validate(Defaults with { Database = database }, Defaults, "allocation.json");
        issues = result.Issues.Count;
        return result.Config;
    }

    [Fact]
    public void Defaults_UseSqliteInTheDataFolder()
    {
        var result = _validator.Validate(Defaults, Defaults, "allocation.json");
        Assert.Empty(result.Issues);
        Assert.Equal(DatabaseType.Sqlite, result.Config.Database.Type);
        Assert.Equal("data/retakev4.db", result.Config.Database.SqliteFile);
    }

    [Theory]
    [InlineData("../../server.db")]
    [InlineData("C:/retake.db")]
    [InlineData("/var/retake.db")]
    [InlineData("data/retake.txt")]
    [InlineData("")]
    public void UnsafeSqliteFile_FallsBackToDefault(string file)
    {
        var config = Validate(new DatabaseConfig { SqliteFile = file }, out var issues);
        Assert.Equal("data/retakev4.db", config.Database.SqliteFile);
        Assert.Equal(1, issues);
    }

    [Fact]
    public void MySqlWithoutConnectionString_FallsBackToSqlite()
    {
        var config = Validate(new DatabaseConfig { Type = DatabaseType.MySql }, out var issues);
        Assert.Equal(DatabaseType.Sqlite, config.Database.Type);
        Assert.Equal(1, issues);
    }

    [Fact]
    public void MySqlWithConnectionString_IsKept()
    {
        var config = Validate(new DatabaseConfig { Type = DatabaseType.MySql, MySqlConnectionString = "Server=db;Database=retake;User ID=retake;Password=x" }, out var issues);
        Assert.Equal(DatabaseType.MySql, config.Database.Type);
        Assert.Equal(0, issues);
    }

    [Fact]
    public void MissingDatabaseSection_FallsBackToDefaults()
    {
        var result = _validator.Validate(Defaults with { Database = null! }, Defaults, "allocation.json");
        Assert.Equal(DatabaseType.Sqlite, result.Config.Database.Type);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Defaults_UseTheMenuMode_AndAFiveMinuteReminder()
    {
        Assert.Equal(AllocationMode.Menu, Defaults.Mode);
        Assert.Equal(5, Defaults.HowToIntervalMinutes);
    }

    [Fact]
    public void UnknownMode_FallsBackToMenu()
    {
        var result = _validator.Validate(Defaults with { Mode = (AllocationMode)9 }, Defaults, "allocation.json");
        Assert.Equal(AllocationMode.Menu, result.Config.Mode);
        Assert.Single(result.Issues);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public void OutOfRangeReminder_FallsBack(int minutes)
    {
        var result = _validator.Validate(Defaults with { HowToIntervalMinutes = minutes }, Defaults, "allocation.json");
        Assert.Equal(5, result.Config.HowToIntervalMinutes);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void DisabledReminder_IsValid() =>
        Assert.Empty(_validator.Validate(Defaults with { HowToIntervalMinutes = 0, Mode = AllocationMode.Both }, Defaults, "allocation.json").Issues);

    [Fact]
    public void ServerKey_DefaultsToDefault() => Assert.Equal("default", Defaults.Database.ServerKey);

    [Theory]
    [InlineData("")]
    [InlineData("my server")]
    [InlineData("a'; DROP TABLE x;--")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public void InvalidServerKey_FallsBackToDefault(string key)
    {
        var config = Validate(new DatabaseConfig { ServerKey = key }, out var issues);
        Assert.Equal("default", config.Database.ServerKey);
        Assert.Equal(1, issues);
    }

    [Theory]
    [InlineData("agora-1")]
    [InlineData("Retake_EU")]
    public void ValidServerKey_IsKept(string key)
    {
        var config = Validate(new DatabaseConfig { ServerKey = key }, out var issues);
        Assert.Equal(key, config.Database.ServerKey);
        Assert.Equal(0, issues);
    }
}

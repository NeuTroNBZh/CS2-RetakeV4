using Microsoft.Data.Sqlite;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Persistence;

public sealed class V3SqliteReaderTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private string CreateV3Database(bool withAwpTable)
    {
        var file = _dir.File("cs2retake.db");
        using var connection = new SqliteConnection($"Data Source={file}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE FullBuyPrimary (UserId INTEGER, WeaponString TEXT, Team INT);
            CREATE TABLE Pistol (UserId INTEGER, WeaponString TEXT, Team INT);
            INSERT INTO FullBuyPrimary VALUES (76561198000000001, 'weapon_m4a1_silencer', 3);
            INSERT INTO FullBuyPrimary VALUES (76561198000000001, 'weapon_ak47', 0);
            INSERT INTO Pistol VALUES (76561198000000002, 'weapon_tec9', 2);
            """;
        if (withAwpTable)
        {
            command.CommandText += "CREATE TABLE FullBuyAWPChance (UserId INTEGER, AWPChance INT, Team INT); INSERT INTO FullBuyAWPChance VALUES (76561198000000001, 30, 3);";
        }
        command.ExecuteNonQuery();
        return file;
    }

    [Fact]
    public async Task ReadsExistingTables_AndSkipsMissingOnes()
    {
        var rows = await V3SqliteReader.ReadAsync(CreateV3Database(withAwpTable: false), CancellationToken.None);
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r is { Table: "FullBuyPrimary", UserId: 76561198000000001UL, Team: 3, WeaponString: "weapon_m4a1_silencer" });
        Assert.Contains(rows, r => r is { Table: "Pistol", WeaponString: "weapon_tec9" });
    }

    [Fact]
    public async Task ReadsAwpChances()
    {
        var rows = await V3SqliteReader.ReadAsync(CreateV3Database(withAwpTable: true), CancellationToken.None);
        Assert.Contains(rows, r => r is { Table: "FullBuyAWPChance", AwpChance: 30, Team: 3 });
    }

    [Fact]
    public async Task MissingFile_Throws() =>
        await Assert.ThrowsAsync<FileNotFoundException>(() => V3SqliteReader.ReadAsync(_dir.File("nope.db"), CancellationToken.None));

    [Fact]
    public async Task NotADatabase_Throws()
    {
        var file = _dir.File("garbage.db");
        await File.WriteAllTextAsync(file, "this is not sqlite");
        await Assert.ThrowsAsync<SqliteException>(() => V3SqliteReader.ReadAsync(file, CancellationToken.None));
    }
}

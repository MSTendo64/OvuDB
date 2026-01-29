using ovudb.Storage;
using Xunit;

namespace ovudb.Tests.Storage;

public class FileStorageTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly FileStorage _storage;

    public FileStorageTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _storage = new FileStorage(_testDataDirectory);
    }

    [Fact]
    public void SaveTable_AndLoadTable_ReturnsSameData()
    {
        var tableName = "TestTable";
        var schema = new Dictionary<string, object>
        {
            ["Columns"] = new List<object> { new { Name = "Id", DataType = "Integer" } }
        };
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Id"] = 1, ["Name"] = "Test" }
        };

        _storage.SaveTable(tableName, schema, rows);
        var loaded = _storage.LoadTable(tableName);

        Assert.NotNull(loaded);
        var (loadedSchema, loadedRows) = loaded.Value;
        Assert.Single(loadedRows);
        
        // When loading from JSON values may be JsonElement, so use GetInt32
        var idValue = loadedRows[0]["Id"];
        var id = idValue is System.Text.Json.JsonElement jsonElement 
            ? jsonElement.GetInt32() 
            : Convert.ToInt32(idValue);
        Assert.Equal(1, id);
        Assert.Equal("Test", loadedRows[0]["Name"].ToString());
    }

    [Fact]
    public void TableExists_AfterSave_ReturnsTrue()
    {
        var tableName = "TestTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        Assert.True(_storage.TableExists(tableName));
    }

    [Fact]
    public void TableExists_BeforeSave_ReturnsFalse()
    {
        Assert.False(_storage.TableExists("NonExistentTable"));
    }

    [Fact]
    public void DeleteTable_RemovesTable()
    {
        var tableName = "TestTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), new List<Dictionary<string, object>>());
        _storage.DeleteTable(tableName);

        Assert.False(_storage.TableExists(tableName));
    }

    [Fact]
    public void GetTableNames_ReturnsAllTables()
    {
        _storage.SaveTable("Table1", new Dictionary<string, object>(), new List<Dictionary<string, object>>());
        _storage.SaveTable("Table2", new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        var tableNames = _storage.GetTableNames();

        Assert.Contains("Table1", tableNames);
        Assert.Contains("Table2", tableNames);
    }

    [Fact]
    public void LoadTable_NonExistent_ReturnsNull()
    {
        var loaded = _storage.LoadTable("NonExistent");
        Assert.Null(loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

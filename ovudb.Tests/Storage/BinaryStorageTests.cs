using ovudb.Core;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.Storage;

public class BinaryStorageTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly BinaryStorage _storage;
    private readonly int _testDatabaseId = 1000;

    public BinaryStorageTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _storage = new BinaryStorage(_testDataDirectory, _testDatabaseId);
    }

    [Fact]
    public void SaveTable_AndLoadTable_ReturnsSameData()
    {
        var tableName = "BinaryTable";
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
        Assert.Equal(1, loadedRows[0]["Id"]);
        Assert.Equal("Test", loadedRows[0]["Name"].ToString());
    }

    [Fact]
    public void TableExists_AfterSave_ReturnsTrue()
    {
        var tableName = "ExistsTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        Assert.True(_storage.TableExists(tableName));
    }

    [Fact]
    public void DeleteTable_RemovesTable()
    {
        var tableName = "DeleteTable";
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
    public void SaveTable_CreatesBinaryFiles()
    {
        var tableName = "BinaryFilesTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        var databaseDir = Path.Combine(_testDataDirectory, _testDatabaseId.ToString());
        var files = Directory.GetFiles(databaseDir, "*.ovu")
            .Where(f => !f.Contains("sys_"))
            .ToList();
        Assert.NotEmpty(files);
    }

    [Fact]
    public void SaveTable_CreatesMetadataFile()
    {
        var tableName = "MetadataFileTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        var databaseDir = Path.Combine(_testDataDirectory, _testDatabaseId.ToString());
        var metaFiles = Directory.GetFiles(databaseDir, "*.ovu.meta");
        Assert.NotEmpty(metaFiles);
    }

    [Fact]
    public void SaveTable_CreatesSystemTables()
    {
        var tableName = "SystemTablesTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        var databaseDir = Path.Combine(_testDataDirectory, _testDatabaseId.ToString());
        Assert.True(File.Exists(Path.Combine(databaseDir, "sys_tables.ovu")));
        Assert.True(File.Exists(Path.Combine(databaseDir, "sys_columns.ovu")));
    }

    [Fact]
    public void LoadTable_WithComplexData_WorksCorrectly()
    {
        var tableName = "ComplexDataTable";
        var schema = new Dictionary<string, object>
        {
            ["Columns"] = new List<object> 
            { 
                new { Name = "Id", DataType = "Integer" },
                new { Name = "Price", DataType = "Double" },
                new { Name = "IsActive", DataType = "Boolean" }
            }
        };
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Id"] = 1, ["Price"] = 99.99, ["IsActive"] = true },
            new() { ["Id"] = 2, ["Price"] = 199.99, ["IsActive"] = false }
        };

        _storage.SaveTable(tableName, schema, rows);
        var loaded = _storage.LoadTable(tableName);

        Assert.NotNull(loaded);
        var (_, loadedRows) = loaded.Value;
        Assert.Equal(2, loadedRows.Count);
        Assert.Equal(1, loadedRows[0]["Id"]);
        Assert.Equal(99.99, Convert.ToDouble(loadedRows[0]["Price"]));
        Assert.True((bool)loadedRows[0]["IsActive"]);
    }

    [Fact]
    public void CreateDump_WorksWithBinaryStorage()
    {
        var tableName = "DumpBinaryTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), 
            new List<Dictionary<string, object>> { new() { ["Id"] = 1 } });

        var dump = _storage.CreateDump(tableName);

        Assert.NotNull(dump);
        Assert.Contains(tableName, dump);
    }

    [Fact]
    public void RestoreFromDump_WorksWithBinaryStorage()
    {
        var tableName = "RestoreBinaryTable";
        var schema = new Dictionary<string, object>();
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Id"] = 1, ["Name"] = "Original" }
        };

        _storage.SaveTable(tableName, schema, rows);
        var dump = _storage.CreateDump(tableName);
        _storage.DeleteTable(tableName);

        _storage.RestoreFromDump(tableName, dump);

        var loaded = _storage.LoadTable(tableName);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Value.rows);
    }

    [Fact]
    public void Database_WithBinaryStorage_Works()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        var dbId = 1001;
        var storage = new BinaryStorage(dataDir, dbId);
        var db = new Database("TestDB", storage);

        var table = db.CreateTable<TestEntity>("BinaryTable");
        table.Insert(new TestEntity { Name = "Test", Age = 25 });

        var all = table.GetAll();
        Assert.Single(all);
        Assert.Equal("Test", all[0].Name);

        // Cleanup
        if (Directory.Exists(dataDir))
        {
            Directory.Delete(dataDir, true);
        }
    }

    [Fact]
    public void MultipleTables_GetUniqueIds()
    {
        _storage.SaveTable("Table1", new Dictionary<string, object>(), new List<Dictionary<string, object>>());
        _storage.SaveTable("Table2", new Dictionary<string, object>(), new List<Dictionary<string, object>>());
        _storage.SaveTable("Table3", new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        var databaseDir = Path.Combine(_testDataDirectory, _testDatabaseId.ToString());
        var dataFiles = Directory.GetFiles(databaseDir, "*.ovu")
            .Where(f => !f.Contains("sys_") && !f.EndsWith(".meta"))
            .ToList();

        Assert.Equal(3, dataFiles.Count);
        // Verify all files have different names (different IDs)
        var fileNames = dataFiles.Select(Path.GetFileNameWithoutExtension).ToList();
        Assert.Equal(3, fileNames.Distinct().Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

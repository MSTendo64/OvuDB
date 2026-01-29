using ovudb.Storage;
using System.Text.Json;
using Xunit;

namespace ovudb.Tests.Storage;

public class FileStorageDumpTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly FileStorage _storage;

    public FileStorageDumpTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _storage = new FileStorage(_testDataDirectory);
    }

    [Fact]
    public void CreateDump_ContainsAllRequiredFields()
    {
        var tableName = "DumpFieldsTable";
        var schema = new Dictionary<string, object>
        {
            ["Columns"] = new List<object> { new { Name = "Id", DataType = "Integer" } }
        };
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Id"] = 1, ["Name"] = "Test" }
        };

        _storage.SaveTable(tableName, schema, rows);
        var dumpJson = _storage.CreateDump(tableName);
        var dump = JsonSerializer.Deserialize<TableDump>(dumpJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(dump);
        Assert.Equal("1.0", dump!.Version);
        Assert.Equal(tableName, dump.TableName);
        Assert.NotNull(dump.Schema);
        Assert.NotNull(dump.Data);
        Assert.Single(dump.Data);
        Assert.Equal(1, dump.RowCount);
    }

    [Fact]
    public void CreateDump_NonExistentTable_ThrowsException()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            _storage.CreateDump("NonExistent");
        });
    }

    [Fact]
    public void RestoreFromDump_ValidDump_RestoresTable()
    {
        var tableName = "RestoreDumpTable";
        var schema = new Dictionary<string, object>
        {
            ["Columns"] = new List<object> { new { Name = "Id", DataType = "Integer" } }
        };
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Id"] = 1, ["Name"] = "Original" },
            new() { ["Id"] = 2, ["Name"] = "Data" }
        };

        _storage.SaveTable(tableName, schema, rows);
        var dump = _storage.CreateDump(tableName);

        // Delete table
        _storage.DeleteTable(tableName);
        Assert.False(_storage.TableExists(tableName));

        // Restore
        _storage.RestoreFromDump(tableName, dump);

        Assert.True(_storage.TableExists(tableName));
        var loaded = _storage.LoadTable(tableName);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Value.rows.Count);
    }

    [Fact]
    public void RestoreFromDump_InvalidJson_ThrowsException()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            _storage.RestoreFromDump("TestTable", "invalid json");
        });
    }

    [Fact]
    public void CreateFullDump_ContainsAllTables()
    {
        _storage.SaveTable("FullDumpTable1", new Dictionary<string, object>(), 
            new List<Dictionary<string, object>> { new() { ["Id"] = 1 } });
        _storage.SaveTable("FullDumpTable2", new Dictionary<string, object>(), 
            new List<Dictionary<string, object>> { new() { ["Id"] = 2 } });

        var fullDumpJson = _storage.CreateFullDump();
        var fullDump = JsonSerializer.Deserialize<FullDatabaseDump>(fullDumpJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(fullDump);
        Assert.Equal(2, fullDump!.Tables.Count);
        Assert.Contains("FullDumpTable1", fullDump.Tables.Keys);
        Assert.Contains("FullDumpTable2", fullDump.Tables.Keys);
    }

    [Fact]
    public void CreateFullDump_ContainsDatabaseMetadata()
    {
        _storage.SaveTable("MetadataDumpTable", new Dictionary<string, object>(), 
            new List<Dictionary<string, object>>());

        var fullDumpJson = _storage.CreateFullDump();
        var fullDump = JsonSerializer.Deserialize<FullDatabaseDump>(fullDumpJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(fullDump);
        Assert.NotNull(fullDump!.DatabaseMetadata);
        Assert.Equal("1.0", fullDump.DatabaseMetadata.Version);
    }

    [Fact]
    public void RestoreFromFullDump_RestoresAllTables()
    {
        _storage.SaveTable("RestoreFull1", new Dictionary<string, object>(), 
            new List<Dictionary<string, object>> { new() { ["Id"] = 1, ["Name"] = "Test1" } });
        _storage.SaveTable("RestoreFull2", new Dictionary<string, object>(), 
            new List<Dictionary<string, object>> { new() { ["Id"] = 2, ["Name"] = "Test2" } });

        var fullDump = _storage.CreateFullDump();

        // Create new storage
        var newDataDir = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        var newStorage = new FileStorage(newDataDir);

        // Restore
        newStorage.RestoreFromFullDump(fullDump);

        Assert.True(newStorage.TableExists("RestoreFull1"));
        Assert.True(newStorage.TableExists("RestoreFull2"));

        var loaded1 = newStorage.LoadTable("RestoreFull1");
        var loaded2 = newStorage.LoadTable("RestoreFull2");

        Assert.NotNull(loaded1);
        Assert.NotNull(loaded2);
        Assert.Single(loaded1.Value.rows);
        Assert.Single(loaded2.Value.rows);

        // Cleanup
        if (Directory.Exists(newDataDir))
        {
            Directory.Delete(newDataDir, true);
        }
    }

    [Fact]
    public void SaveDumpToFile_SavesCorrectly()
    {
        var tableName = "SaveDumpFileTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), 
            new List<Dictionary<string, object>> { new() { ["Id"] = 1 } });

        var dump = _storage.CreateDump(tableName);
        _storage.SaveDumpToFile(tableName, dump);

        var dumpPath = Path.Combine(_testDataDirectory, $"{tableName}.dump.json");
        Assert.True(File.Exists(dumpPath));

        var savedDump = File.ReadAllText(dumpPath);
        Assert.Equal(dump, savedDump);
    }

    [Fact]
    public void LoadDumpFromFile_LoadsCorrectly()
    {
        var tableName = "LoadDumpFileTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), 
            new List<Dictionary<string, object>> { new() { ["Id"] = 1 } });

        var originalDump = _storage.CreateDump(tableName);
        _storage.SaveDumpToFile(tableName, originalDump);

        var loadedDump = _storage.LoadDumpFromFile(tableName);
        Assert.Equal(originalDump, loadedDump);
    }

    [Fact]
    public void LoadDumpFromFile_NonExistent_ThrowsException()
    {
        Assert.Throws<FileNotFoundException>(() =>
        {
            _storage.LoadDumpFromFile("NonExistent");
        });
    }

    [Fact]
    public void GetTableNames_ExcludesDumpFiles()
    {
        _storage.SaveTable("NormalTable", new Dictionary<string, object>(), 
            new List<Dictionary<string, object>>());
        
        var dump = _storage.CreateDump("NormalTable");
        _storage.SaveDumpToFile("NormalTable", dump);

        var tableNames = _storage.GetTableNames();
        Assert.Contains("NormalTable", tableNames);
        Assert.Single(tableNames);
    }

    [Fact]
    public void GetTableNames_ExcludesMetadataFile()
    {
        _storage.SaveTable("TestTable", new Dictionary<string, object>(), 
            new List<Dictionary<string, object>>());

        var tableNames = _storage.GetTableNames();
        Assert.DoesNotContain("_metadata", tableNames);
        Assert.Contains("TestTable", tableNames);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

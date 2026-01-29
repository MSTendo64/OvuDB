using ovudb.Core;
using ovudb.Storage;
using ovudb.Tests.Models;
using System.Text.Json;
using Xunit;

namespace ovudb.Tests.Storage;

public class MetadataTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly FileStorage _storage;

    public MetadataTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _storage = new FileStorage(_testDataDirectory);
    }

    [Fact]
    public void InitializeMetadata_CreatesMetadataFile()
    {
        var metadataPath = Path.Combine(_testDataDirectory, "_metadata.json");
        Assert.True(File.Exists(metadataPath));
    }

    [Fact]
    public void SaveTable_UpdatesMetadata()
    {
        var tableName = "MetadataTable";
        var schema = new Dictionary<string, object>
        {
            ["Columns"] = new List<object> { new { Name = "Id", DataType = "Integer" } }
        };
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Id"] = 1, ["Name"] = "Test1" },
            new() { ["Id"] = 2, ["Name"] = "Test2" }
        };

        _storage.SaveTable(tableName, schema, rows);

        var metadataPath = Path.Combine(_testDataDirectory, "_metadata.json");
        var metadataJson = File.ReadAllText(metadataPath);
        var metadata = JsonSerializer.Deserialize<DatabaseMetadata>(metadataJson);

        Assert.NotNull(metadata);
        Assert.True(metadata!.Tables.ContainsKey(tableName));
        Assert.Equal(2, metadata.Tables[tableName].RowCount);
    }

    [Fact]
    public void DeleteTable_RemovesFromMetadata()
    {
        var tableName = "DeleteMetadataTable";
        _storage.SaveTable(tableName, new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        var metadataPath = Path.Combine(_testDataDirectory, "_metadata.json");
        var metadataJson = File.ReadAllText(metadataPath);
        var metadataBefore = JsonSerializer.Deserialize<DatabaseMetadata>(metadataJson);
        Assert.True(metadataBefore!.Tables.ContainsKey(tableName));

        _storage.DeleteTable(tableName);

        var metadataJsonAfter = File.ReadAllText(metadataPath);
        var metadataAfter = JsonSerializer.Deserialize<DatabaseMetadata>(metadataJsonAfter);
        Assert.False(metadataAfter!.Tables.ContainsKey(tableName));
    }

    [Fact]
    public void SaveTable_UpdatesLastModified()
    {
        var tableName = "ModifiedTable";
        var schema = new Dictionary<string, object>();
        var rows = new List<Dictionary<string, object>>();

        _storage.SaveTable(tableName, schema, rows);
        var firstModified = GetTableMetadata(tableName)?.LastModified;

        // Wait a bit for time to change
        Thread.Sleep(10);

        rows.Add(new Dictionary<string, object> { ["Id"] = 1 });
        _storage.SaveTable(tableName, schema, rows);
        var secondModified = GetTableMetadata(tableName)?.LastModified;

        Assert.True(secondModified > firstModified);
    }

    [Fact]
    public void TableFile_ContainsMetadata()
    {
        var tableName = "FileMetadataTable";
        var schema = new Dictionary<string, object>();
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Id"] = 1 }
        };

        _storage.SaveTable(tableName, schema, rows);

        var tablePath = Path.Combine(_testDataDirectory, $"{tableName}.json");
        var tableJson = File.ReadAllText(tablePath);
        var tableData = JsonSerializer.Deserialize<TableData>(tableJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(tableData);
        Assert.NotNull(tableData!.Metadata);
        Assert.Equal(tableName, tableData.Metadata.TableName);
        Assert.Equal(1, tableData.Metadata.RowCount);
    }

    [Fact]
    public void MultipleTables_AllInMetadata()
    {
        _storage.SaveTable("Table1", new Dictionary<string, object>(), new List<Dictionary<string, object>>());
        _storage.SaveTable("Table2", new Dictionary<string, object>(), new List<Dictionary<string, object>>());
        _storage.SaveTable("Table3", new Dictionary<string, object>(), new List<Dictionary<string, object>>());

        var metadata = GetDatabaseMetadata();
        Assert.Equal(3, metadata.Tables.Count);
        Assert.Contains("Table1", metadata.Tables.Keys);
        Assert.Contains("Table2", metadata.Tables.Keys);
        Assert.Contains("Table3", metadata.Tables.Keys);
    }

    [Fact]
    public void Metadata_ContainsVersion()
    {
        var metadata = GetDatabaseMetadata();
        Assert.Equal("1.0", metadata.Version);
    }

    [Fact]
    public void Metadata_ContainsTimestamps()
    {
        var metadata = GetDatabaseMetadata();
        Assert.True(metadata.CreatedAt > DateTime.MinValue);
        Assert.True(metadata.LastModified > DateTime.MinValue);
    }

    private DatabaseMetadata GetDatabaseMetadata()
    {
        var metadataPath = Path.Combine(_testDataDirectory, "_metadata.json");
        var metadataJson = File.ReadAllText(metadataPath);
        return JsonSerializer.Deserialize<DatabaseMetadata>(metadataJson) ?? new DatabaseMetadata();
    }

    private TableMetadata? GetTableMetadata(string tableName)
    {
        var metadata = GetDatabaseMetadata();
        return metadata.Tables.GetValueOrDefault(tableName);
    }

    private class DatabaseMetadata
    {
        public string Version { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
        public Dictionary<string, TableMetadata> Tables { get; set; } = new();
    }

    private class TableData
    {
        public Dictionary<string, object> Schema { get; set; } = new();
        public List<Dictionary<string, object>> Rows { get; set; } = new();
        public TableFileMetadata? Metadata { get; set; }
    }

    private class TableFileMetadata
    {
        public string TableName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
        public int RowCount { get; set; }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

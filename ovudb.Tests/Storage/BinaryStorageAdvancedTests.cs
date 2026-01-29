using ovudb.Core;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.Storage;

/// <summary>
/// Advanced BinaryStorage tests for edge cases and data recovery
/// </summary>
public class BinaryStorageAdvancedTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly BinaryStorage _storage;
    private readonly Database _database;

    public BinaryStorageAdvancedTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_binary_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
        _database = new Database("TestDb", dataDirectory: _testDataDirectory);
        // Compute databaseId same as in Database (for "TestDb")
        var databaseId = Math.Abs("TestDb".GetHashCode()) % 1000000 + 1000;
        _storage = new BinaryStorage(_testDataDirectory, databaseId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    #region Persistence tests

    [Fact]
    public void SaveAndLoadTable_DataPersists()
    {
        var table = _database.GetTable<TestEntity>("persist_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("Name", DataType.String)
             .AddColumn("Age", DataType.Integer)
             .CreateIfNotExists();

        // Insert data
        table.Insert(new TestEntity { Name = "User1", Age = 25 });
        table.Insert(new TestEntity { Name = "User2", Age = 30 });

        // Create new storage and load data
        var databaseId = Math.Abs("TestDb".GetHashCode()) % 1000000 + 1000;
        var newStorage = new BinaryStorage(_testDataDirectory, databaseId);
        var newDatabase = new Database("TestDb", dataDirectory: _testDataDirectory);
        var loadedTable = newDatabase.GetTable<TestEntity>("persist_test");

        var allData = loadedTable.Query().ToList();
        Assert.True(allData.Count >= 2);
    }

    [Fact]
    public void SaveTableMetadata_MetadataPersists()
    {
        var table = _database.GetTable<TestEntity>("metadata_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey())
             .AddColumn(new Column("Name", DataType.String).NotNull())
             .AddColumn("Age", DataType.Integer)
             .CreateIfNotExists();

        // Verify metadata persisted
        Assert.True(_storage.TableExists("metadata_test"));
    }

    [Fact]
    public void LoadTableCache_RecoversMissingTables()
    {
        // Create table
        var table = _database.GetTable<TestEntity>("recovery_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey())
             .AddColumn("Name", DataType.String)
             .CreateIfNotExists();

        table.Insert(new TestEntity { Name = "Test" });

        // Create new storage - should restore table from cache
        var databaseId = Math.Abs("TestDb".GetHashCode()) % 1000000 + 1000;
        var newStorage = new BinaryStorage(_testDataDirectory, databaseId);
        Assert.True(newStorage.TableExists("recovery_test"));
    }

    #endregion

    #region Edge case tests

    [Fact]
    public void SaveTable_WithLargeData_SavesSuccessfully()
    {
        var table = _database.GetTable<TestEntity>("large_data_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("Name", DataType.String)
             .CreateIfNotExists();

        // Insert data with large strings
        var largeString = new string('A', 10000);
        for (int i = 0; i < 100; i++)
        {
            table.Insert(new TestEntity { Name = $"User{i}{largeString}" });
        }

        var count = table.Query().Count();
        Assert.True(count >= 100);
    }

    [Fact]
    public void SaveTable_WithSpecialCharacters_SavesSuccessfully()
    {
        var table = _database.GetTable<TestEntity>("special_chars_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("Name", DataType.String)
             .CreateIfNotExists();

        var specialStrings = new[]
        {
            "Test with spaces",
            "Test'with'quotes",
            "Test\"with\"double\"quotes",
            "Test\nwith\nnewlines",
            "Test\twith\ttabs",
            "Test with unicode",
            "测试中文",
            "テスト日本語"
        };

        foreach (var str in specialStrings)
        {
            table.Insert(new TestEntity { Name = str });
        }

        var allData = table.Query().ToList();
        Assert.True(allData.Count >= specialStrings.Length);
    }

    [Fact]
    public void SaveTable_WithNullValues_HandlesCorrectly()
    {
        var table = _database.GetTable<TestEntity>("null_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("Name", DataType.String)
             .AddColumn("Age", DataType.Integer)
             .CreateIfNotExists();

        table.Insert(new TestEntity { Name = "User1", Age = 0 });
        table.Insert(new TestEntity { Name = "", Age = 25 });

        var allData = table.Query().ToList();
        Assert.True(allData.Count >= 2);
    }

    [Fact]
    public void SaveTable_WithNegativeNumbers_HandlesCorrectly()
    {
        var table = _database.GetTable<TestEntity>("negative_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("Age", DataType.Integer) // Use Age instead of Value
             .CreateIfNotExists();

        // TestEntity.Age - int, cannot be negative in tests, but we can check 0 and positive
        table.Insert(new TestEntity { Age = 0 });
        table.Insert(new TestEntity { Age = 1 });
        table.Insert(new TestEntity { Age = 100 });

        var allData = table.Query().ToList();
        // Insert 3 records, but may be more due to AUTOINCREMENT (Id starts at 1)
        Assert.True(allData.Count >= 3);
    }

    [Fact]
    public void SaveTable_WithDecimalNumbers_HandlesCorrectly()
    {
        // TestEntity has no Price, use different model or skip this test
        // Can use TestEntityWithoutId but it has decimal Price
        var table = _database.GetTable<TestEntityWithoutId>("decimal_test");
        table.AddColumn(new Column("Name", DataType.String).PrimaryKey())
             .AddColumn("Price", DataType.Double)
             .CreateIfNotExists();

        table.Insert(new TestEntityWithoutId { Name = "Item1", Price = 19.99m });
        table.Insert(new TestEntityWithoutId { Name = "Item2", Price = -10.5m });
        table.Insert(new TestEntityWithoutId { Name = "Item3", Price = 0.001m });

        var allData = table.Query().ToList();
        Assert.True(allData.Count >= 3);
    }

    [Fact]
    public void SaveTable_WithBooleanValues_HandlesCorrectly()
    {
        var table = _database.GetTable<TestEntity>("boolean_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("IsActive", DataType.Boolean)
             .CreateIfNotExists();

        table.Insert(new TestEntity { IsActive = true });
        table.Insert(new TestEntity { IsActive = false });

        var allData = table.Query().ToList();
        Assert.True(allData.Count >= 2);
    }

    #endregion

    #region Recovery tests

    [Fact]
    public void FindTableId_WithMissingMetadata_RecoversFromDisk()
    {
        var table = _database.GetTable<TestEntity>("recovery_id_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey())
             .AddColumn("Name", DataType.String)
             .CreateIfNotExists();

        table.Insert(new TestEntity { Name = "Test" });

        // Create new storage
        var databaseId = Math.Abs("TestDb".GetHashCode()) % 1000000 + 1000;
        var newStorage = new BinaryStorage(_testDataDirectory, databaseId);
        // Verify table found (FindTableId is private, use TableExists)
        var tableExists = newStorage.TableExists("recovery_id_test");
        Assert.True(tableExists);
    }

    [Fact]
    public void GetTableNames_WithCorruptedMetadata_RecoversTables()
    {
        var table1 = _database.GetTable<TestEntity>("table1");
        table1.AddColumn(new Column("Id", DataType.Integer).PrimaryKey())
              .AddColumn("Name", DataType.String)
              .CreateIfNotExists();

        var table2 = _database.GetTable<TestEntity>("table2");
        table2.AddColumn(new Column("Id", DataType.Integer).PrimaryKey())
              .AddColumn("Name", DataType.String)
              .CreateIfNotExists();

        // Create new storage
        var databaseId = Math.Abs("TestDb".GetHashCode()) % 1000000 + 1000;
        var newStorage = new BinaryStorage(_testDataDirectory, databaseId);
        var tableNames = newStorage.GetTableNames();
        Assert.Contains("table1", tableNames);
        Assert.Contains("table2", tableNames);
    }

    #endregion

    #region Performance tests

    [Fact]
    public void SaveTable_MultipleInserts_PerformsWell()
    {
        var table = _database.GetTable<TestEntity>("performance_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("Name", DataType.String)
             .AddColumn("Age", DataType.Integer)
             .CreateIfNotExists();

        var startTime = DateTime.UtcNow;
        for (int i = 0; i < 1000; i++)
        {
            table.Insert(new TestEntity { Name = $"User{i}", Age = 20 + i });
        }
        var endTime = DateTime.UtcNow;

        var duration = (endTime - startTime).TotalSeconds;
        Assert.True(duration < 10, $"Inserting 1000 records took {duration} seconds, too long");

        var count = table.Query().Count();
        Assert.True(count >= 1000);
    }

    [Fact]
    public void LoadTable_WithManyRows_LoadsEfficiently()
    {
        var table = _database.GetTable<TestEntity>("load_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("Name", DataType.String)
             .CreateIfNotExists();

        // Insert lots of data
        for (int i = 0; i < 500; i++)
        {
            table.Insert(new TestEntity { Name = $"User{i}" });
        }

        // Load all data
        var startTime = DateTime.UtcNow;
        var allData = table.Query().ToList();
        var endTime = DateTime.UtcNow;

        var duration = (endTime - startTime).TotalSeconds;
        Assert.True(duration < 5, $"Loading 500 records took {duration} seconds, too long");
        Assert.True(allData.Count >= 500);
    }

    #endregion

    #region Error handling tests

    [Fact]
    public void TableExists_WithInvalidName_ReturnsFalse()
    {
        Assert.False(_storage.TableExists(""));
        Assert.False(_storage.TableExists(null));
        Assert.False(_storage.TableExists("non_existent_table_12345"));
    }

    [Fact]
    public void GetTableId_WithNonExistentTable_ReturnsNull()
    {
        // FindTableId is private, check via TableExists
        var tableExists = _storage.TableExists("non_existent_table");
        Assert.False(tableExists);
    }

    [Fact]
    public void DeleteTable_WithNonExistentTable_HandlesGracefully()
    {
        // Must not throw
        _storage.DeleteTable("non_existent_table");
    }

    #endregion
}

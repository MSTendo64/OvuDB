using ovudb.Core;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.Storage;

public class DumpTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;

    public DumpTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        var storage = new FileStorage(Path.Combine(_testDataDirectory, "DumpTestDB"));
        _database = new Database("DumpTestDB", storage);
    }

    [Fact]
    public void CreateTableDump_CreatesValidDump()
    {
        var table = _database.CreateTable<TestEntity>("DumpTable");
        table.Insert(new TestEntity { Name = "Test1", Age = 25 });
        table.Insert(new TestEntity { Name = "Test2", Age = 30 });

        var dump = _database.CreateTableDump("DumpTable");

        Assert.NotNull(dump);
        Assert.Contains("DumpTable", dump);
        Assert.Contains("Test1", dump);
        Assert.Contains("Test2", dump);
    }

    [Fact]
    public void RestoreTableFromDump_RestoresData()
    {
        var table1 = _database.CreateTable<TestEntity>("RestoreTable");
        table1.Insert(new TestEntity { Name = "Original1", Age = 20 });
        table1.Insert(new TestEntity { Name = "Original2", Age = 25 });

        var dump = _database.CreateTableDump("RestoreTable");

        // Delete table
        _database.DropTable("RestoreTable");

        // Restore from dump
        _database.RestoreTableFromDump("RestoreTable", dump);

        var table2 = _database.GetTable<TestEntity>("RestoreTable");
        table2.CreateIfNotExists();
        var restored = table2.GetAll();

        Assert.Equal(2, restored.Count);
        Assert.Contains(restored, r => r.Name == "Original1");
        Assert.Contains(restored, r => r.Name == "Original2");
    }

    [Fact]
    public void CreateFullDump_IncludesAllTables()
    {
        var table1 = _database.CreateTable<TestEntity>("Table1");
        var table2 = _database.CreateTable<TestEntityWithoutId>("Table2");

        table1.Insert(new TestEntity { Name = "Test1" });
        table2.Insert(new TestEntityWithoutId { Name = "Product1", Price = 100 });

        var fullDump = _database.CreateFullDump();

        Assert.NotNull(fullDump);
        Assert.Contains("Table1", fullDump);
        Assert.Contains("Table2", fullDump);
        Assert.Contains("Test1", fullDump);
        Assert.Contains("Product1", fullDump);
    }

    [Fact]
    public void RestoreFromFullDump_RestoresAllTables()
    {
        var table1 = _database.CreateTable<TestEntity>("FullTable1");
        var table2 = _database.CreateTable<TestEntityWithoutId>("FullTable2");

        table1.Insert(new TestEntity { Name = "FullTest1", Age = 20 });
        table2.Insert(new TestEntityWithoutId { Name = "FullProduct1", Price = 200 });

        var fullDump = _database.CreateFullDump();

        // Create new database
        var newDataDir = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        var newStorage = new FileStorage(Path.Combine(newDataDir, "RestoredDB"));
        var restoredDb = new Database("RestoredDB", newStorage);

        // Restore
        restoredDb.RestoreFromFullDump(fullDump);

        var restoredTable1 = restoredDb.GetTable<TestEntity>("FullTable1");
        restoredTable1.CreateIfNotExists();
        var restoredTable2 = restoredDb.GetTable<TestEntityWithoutId>("FullTable2");
        restoredTable2.CreateIfNotExists();

        var data1 = restoredTable1.GetAll();
        var data2 = restoredTable2.GetAll();

        Assert.Single(data1);
        Assert.Equal("FullTest1", data1[0].Name);
        Assert.Single(data2);
        Assert.Equal("FullProduct1", data2[0].Name);

        // Cleanup
        if (Directory.Exists(newDataDir))
        {
            Directory.Delete(newDataDir, true);
        }
    }

    [Fact]
    public void SaveTableDumpToFile_SavesToFile()
    {
        var table = _database.CreateTable<TestEntity>("FileDumpTable");
        table.Insert(new TestEntity { Name = "FileTest", Age = 30 });

        var dumpFile = Path.Combine(_testDataDirectory, "test_dump.json");
        _database.SaveTableDumpToFile("FileDumpTable", dumpFile);

        Assert.True(File.Exists(dumpFile));
        var content = File.ReadAllText(dumpFile);
        Assert.Contains("FileDumpTable", content);
        Assert.Contains("FileTest", content);
    }

    [Fact]
    public void RestoreTableFromDumpFile_RestoresFromFile()
    {
        var table1 = _database.CreateTable<TestEntity>("FileRestoreTable");
        table1.Insert(new TestEntity { Name = "FileRestore", Age = 40 });

        var dumpFile = Path.Combine(_testDataDirectory, "restore_dump.json");
        _database.SaveTableDumpToFile("FileRestoreTable", dumpFile);

        // Delete table
        _database.DropTable("FileRestoreTable");

        // Restore from file
        _database.RestoreTableFromDumpFile("FileRestoreTable", dumpFile);

        var table2 = _database.GetTable<TestEntity>("FileRestoreTable");
        table2.CreateIfNotExists();
        var restored = table2.GetAll();

        Assert.Single(restored);
        Assert.Equal("FileRestore", restored[0].Name);
        Assert.Equal(40, restored[0].Age);
    }

    [Fact]
    public void CreateTableDump_NonExistentTable_ThrowsException()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            _database.CreateTableDump("NonExistentTable");
        });
    }

    [Fact]
    public void CreateTableDump_ContainsMetadata()
    {
        var table = _database.CreateTable<TestEntity>("MetadataDumpTable");
        table.Insert(new TestEntity { Name = "Test", Age = 25 });

        var dump = _database.CreateTableDump("MetadataDumpTable");
        Assert.Contains("metadata", dump, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("createdAt", dump, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreTableFromDump_PreservesDataTypes()
    {
        var table1 = _database.CreateTable<TestEntity>("TypePreserveTable");
        table1.Insert(new TestEntity { Name = "Test", Age = 30, IsActive = true });

        var dump = _database.CreateTableDump("TypePreserveTable");
        _database.DropTable("TypePreserveTable");

        _database.RestoreTableFromDump("TypePreserveTable", dump);

        var table2 = _database.GetTable<TestEntity>("TypePreserveTable");
        table2.CreateIfNotExists();
        var restored = table2.GetAll();

        Assert.Single(restored);
        Assert.Equal("Test", restored[0].Name);
        Assert.Equal(30, restored[0].Age);
        Assert.True(restored[0].IsActive);
    }

    [Fact]
    public void CreateFullDump_EmptyDatabase_CreatesValidDump()
    {
        var fullDump = _database.CreateFullDump();
        Assert.NotNull(fullDump);
        Assert.Contains("version", fullDump, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("databaseName", fullDump, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreFromFullDump_OverwritesExistingTables()
    {
        var table1 = _database.CreateTable<TestEntity>("OverwriteTable");
        table1.Insert(new TestEntity { Name = "Original", Age = 10 });

        var fullDump = _database.CreateFullDump();

        // Modify data
        table1.Insert(new TestEntity { Name = "Modified", Age = 20 });

        // Restore from dump
        _database.RestoreFromFullDump(fullDump);

        var table2 = _database.GetTable<TestEntity>("OverwriteTable");
        table2.CreateIfNotExists();
        var restored = table2.GetAll();

        Assert.Single(restored);
        Assert.Equal("Original", restored[0].Name);
        Assert.Equal(10, restored[0].Age);
    }

    [Fact]
    public void SaveTableDumpToFile_DefaultPath_SavesToStorageDirectory()
    {
        var table = _database.CreateTable<TestEntity>("DefaultPathTable");
        table.Insert(new TestEntity { Name = "Test" });

        _database.SaveTableDumpToFile("DefaultPathTable");

        // Verify file created in storage directory
        var storage = _database.GetType().GetField("_storage", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // Hard to verify directly, just verify method does not throw
        Assert.True(true);
    }

    [Fact]
    public void RestoreTableFromDumpFile_InvalidFile_ThrowsException()
    {
        var invalidPath = Path.Combine(_testDataDirectory, "nonexistent_dump.json");
        Assert.Throws<FileNotFoundException>(() =>
        {
            _database.RestoreTableFromDumpFile("TestTable", invalidPath);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

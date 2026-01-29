using ovudb.Core;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.Core;

public class DatabaseTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;

    public DatabaseTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        var storage = new FileStorage(Path.Combine(_testDataDirectory, "TestDB"));
        _database = new Database("TestDB", storage);
    }

    [Fact]
    public void CreateTable_CreatesTable()
    {
        var table = _database.CreateTable<TestEntity>("TestTable");
        
        Assert.NotNull(table);
        Assert.True(_database.TableExists("TestTable"));
    }

    [Fact]
    public void GetTable_ReturnsTable()
    {
        var table = _database.GetTable<TestEntity>("TestTable");
        
        Assert.NotNull(table);
    }

    [Fact]
    public void GetTable_SameName_ReturnsSameInstance()
    {
        var table1 = _database.GetTable<TestEntity>("TestTable");
        var table2 = _database.GetTable<TestEntity>("TestTable");
        
        Assert.Same(table1, table2);
    }

    [Fact]
    public void DropTable_RemovesTable()
    {
        _database.CreateTable<TestEntity>("TestTable");
        Assert.True(_database.TableExists("TestTable"));
        
        _database.DropTable("TestTable");
        
        Assert.False(_database.TableExists("TestTable"));
    }

    [Fact]
    public void GetTableNames_ReturnsAllTables()
    {
        _database.CreateTable<TestEntity>("Table1");
        _database.CreateTable<TestEntity>("Table2");
        
        var tableNames = _database.GetTableNames();
        
        Assert.Contains("Table1", tableNames);
        Assert.Contains("Table2", tableNames);
    }

    [Fact]
    public void TableExists_AfterCreate_ReturnsTrue()
    {
        _database.CreateTable<TestEntity>("TestTable");
        
        Assert.True(_database.TableExists("TestTable"));
    }

    [Fact]
    public void TableExists_BeforeCreate_ReturnsFalse()
    {
        Assert.False(_database.TableExists("NonExistentTable"));
    }

    [Fact]
    public void Name_ReturnsDatabaseName()
    {
        Assert.Equal("TestDB", _database.Name);
    }

    [Fact]
    public void MultipleTables_WorkIndependently()
    {
        var table1 = _database.CreateTable<TestEntity>("Table1");
        var table2 = _database.CreateTable<TestEntityWithoutId>("Table2");
        
        table1.Insert(new TestEntity { Name = "Test1" });
        table2.Insert(new TestEntityWithoutId { Name = "Test2", Price = 100 });
        
        Assert.Single(table1.GetAll());
        Assert.Single(table2.GetAll());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

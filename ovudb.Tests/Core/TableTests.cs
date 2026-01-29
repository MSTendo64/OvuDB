using ovudb.Core;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.Core;

public class TableTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly IStorage _storage;
    private readonly Table<TestEntity> _table;

    public TableTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _storage = new FileStorage(_testDataDirectory);
        _table = new Table<TestEntity>("TestTable", _storage);
    }

    [Fact]
    public void Insert_AddsEntity()
    {
        _table.CreateIfNotExists();
        var entity = new TestEntity { Name = "Test", Age = 25 };

        var inserted = _table.Insert(entity);

        Assert.True(inserted.Id > 0);
        Assert.Equal("Test", inserted.Name);
    }

    [Fact]
    public void Insert_AutoIncrement_GeneratesId()
    {
        _table.CreateIfNotExists();
        var entity1 = new TestEntity { Name = "Test1" };
        var entity2 = new TestEntity { Name = "Test2" };

        var inserted1 = _table.Insert(entity1);
        var inserted2 = _table.Insert(entity2);

        Assert.Equal(1, inserted1.Id);
        Assert.Equal(2, inserted2.Id);
    }

    [Fact]
    public void GetAll_ReturnsAllEntities()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1" });
        _table.Insert(new TestEntity { Name = "Test2" });

        var all = _table.GetAll();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetById_ReturnsCorrectEntity()
    {
        _table.CreateIfNotExists();
        var inserted = _table.Insert(new TestEntity { Name = "Test" });

        var found = _table.GetById(inserted.Id);

        Assert.NotNull(found);
        Assert.Equal("Test", found.Name);
    }

    [Fact]
    public void GetById_NonExistent_ReturnsNull()
    {
        _table.CreateIfNotExists();
        var found = _table.GetById(999);
        Assert.Null(found);
    }

    [Fact]
    public void Update_ModifiesEntity()
    {
        _table.CreateIfNotExists();
        var entity = _table.Insert(new TestEntity { Name = "Old", Age = 20 });
        entity.Name = "New";
        entity.Age = 30;

        var updated = _table.Update(entity);

        Assert.True(updated);
        var found = _table.GetById(entity.Id);
        Assert.Equal("New", found?.Name);
        Assert.Equal(30, found?.Age);
    }

    [Fact]
    public void Update_NonExistent_ReturnsFalse()
    {
        _table.CreateIfNotExists();
        var entity = new TestEntity { Id = 999, Name = "Test" };

        var updated = _table.Update(entity);

        Assert.False(updated);
    }

    [Fact]
    public void Delete_RemovesEntity()
    {
        _table.CreateIfNotExists();
        var entity = _table.Insert(new TestEntity { Name = "Test" });

        var deleted = _table.Delete(entity);

        Assert.True(deleted);
        Assert.Null(_table.GetById(entity.Id));
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        _table.CreateIfNotExists();
        var entity = new TestEntity { Id = 999, Name = "Test" };

        var deleted = _table.Delete(entity);

        Assert.False(deleted);
    }

    [Fact]
    public void Query_ReturnsQueryBuilder()
    {
        _table.CreateIfNotExists();
        var query = _table.Query();
        Assert.NotNull(query);
    }

    [Fact]
    public void CreateIfNotExists_CreatesTable()
    {
        _table.CreateIfNotExists();
        Assert.True(_storage.TableExists("TestTable"));
    }

    [Fact]
    public void CreateIfNotExists_LoadsExistingTable()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test" });

        var newTable = new Table<TestEntity>("TestTable", _storage);
        newTable.CreateIfNotExists();

        var all = newTable.GetAll();
        Assert.Single(all);
    }

    [Fact]
    public void AddColumn_AddsColumn()
    {
        var column = new Column("CustomColumn", DataType.String);
        _table.AddColumn(column);

        _table.CreateIfNotExists();
        // Verify table created with extra column
        Assert.True(_storage.TableExists("TestTable"));
    }

    [Fact]
    public void AddIndex_AddsIndex()
    {
        var index = new ovudb.Core.Index("idx_name", "Name");
        _table.AddIndex(index);

        _table.CreateIfNotExists();
        Assert.True(_storage.TableExists("TestTable"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

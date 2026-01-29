using ovudb.Core;
using ovudb.Query;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.Query;

public class QueryBuilderTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly IStorage _storage;
    private readonly Table<TestEntity> _table;

    public QueryBuilderTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _storage = new FileStorage(_testDataDirectory);
        _table = new Table<TestEntity>("TestTable", _storage);
    }

    [Fact]
    public void Where_StringCondition_FiltersResults()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20 });
        _table.Insert(new TestEntity { Name = "Test2", Age = 30 });

        var results = _table.Query()
            .Where("Age", 25, ComparisonOperator.GreaterThan)
            .ToList();

        Assert.Single(results);
        Assert.Equal("Test2", results[0].Name);
    }

    [Fact]
    public void Where_ExpressionCondition_FiltersResults()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", IsActive = true });
        _table.Insert(new TestEntity { Name = "Test2", IsActive = false });

        var results = _table.Query()
            .Where(e => e.IsActive == true)
            .ToList();

        Assert.Single(results);
        Assert.Equal("Test1", results[0].Name);
    }

    [Fact]
    public void Where_Equals_FiltersResults()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20 });
        _table.Insert(new TestEntity { Name = "Test2", Age = 30 });

        var results = _table.Query()
            .Where("Age", 20)
            .ToList();

        Assert.Single(results);
        Assert.Equal("Test1", results[0].Name);
    }

    [Fact]
    public void Where_Like_FiltersResults()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1" });
        _table.Insert(new TestEntity { Name = "Other" });

        var results = _table.Query()
            .Where("Name", "Test", ComparisonOperator.Like)
            .ToList();

        Assert.Single(results);
        Assert.Equal("Test1", results[0].Name);
    }

    [Fact]
    public void OrderBy_SortsAscending()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "C", Age = 30 });
        _table.Insert(new TestEntity { Name = "A", Age = 10 });
        _table.Insert(new TestEntity { Name = "B", Age = 20 });

        var results = _table.Query()
            .OrderBy("Age")
            .ToList();

        Assert.Equal(10, results[0].Age);
        Assert.Equal(20, results[1].Age);
        Assert.Equal(30, results[2].Age);
    }

    [Fact]
    public void OrderByDescending_SortsDescending()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "A", Age = 10 });
        _table.Insert(new TestEntity { Name = "B", Age = 20 });
        _table.Insert(new TestEntity { Name = "C", Age = 30 });

        var results = _table.Query()
            .OrderByDescending("Age")
            .ToList();

        Assert.Equal(30, results[0].Age);
        Assert.Equal(20, results[1].Age);
        Assert.Equal(10, results[2].Age);
    }

    [Fact]
    public void Limit_ReturnsLimitedResults()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1" });
        _table.Insert(new TestEntity { Name = "Test2" });
        _table.Insert(new TestEntity { Name = "Test3" });

        var results = _table.Query()
            .Limit(2)
            .ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Offset_SkipsRecords()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 10 });
        _table.Insert(new TestEntity { Name = "Test2", Age = 20 });
        _table.Insert(new TestEntity { Name = "Test3", Age = 30 });

        var results = _table.Query()
            .OrderBy("Age")
            .Offset(1)
            .ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(20, results[0].Age);
    }

    [Fact]
    public void LimitAndOffset_WorksTogether()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 10 });
        _table.Insert(new TestEntity { Name = "Test2", Age = 20 });
        _table.Insert(new TestEntity { Name = "Test3", Age = 30 });
        _table.Insert(new TestEntity { Name = "Test4", Age = 40 });

        var results = _table.Query()
            .OrderBy("Age")
            .Offset(1)
            .Limit(2)
            .ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(20, results[0].Age);
        Assert.Equal(30, results[1].Age);
    }

    [Fact]
    public void Count_ReturnsCorrectCount()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20 });
        _table.Insert(new TestEntity { Name = "Test2", Age = 30 });
        _table.Insert(new TestEntity { Name = "Test3", Age = 20 });

        var count = _table.Query()
            .Where("Age", 20)
            .Count();

        Assert.Equal(2, count);
    }

    [Fact]
    public void Any_ReturnsTrue_WhenRecordsExist()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20 });

        var any = _table.Query()
            .Where("Age", 20)
            .Any();

        Assert.True(any);
    }

    [Fact]
    public void Any_ReturnsFalse_WhenNoRecords()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20 });

        var any = _table.Query()
            .Where("Age", 999)
            .Any();

        Assert.False(any);
    }

    [Fact]
    public void FirstOrDefault_ReturnsFirst()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20 });
        _table.Insert(new TestEntity { Name = "Test2", Age = 30 });

        var first = _table.Query()
            .OrderBy("Age")
            .FirstOrDefault();

        Assert.NotNull(first);
        Assert.Equal("Test1", first.Name);
    }

    [Fact]
    public void FirstOrDefault_NoResults_ReturnsNull()
    {
        _table.CreateIfNotExists();

        var first = _table.Query()
            .Where("Age", 999)
            .FirstOrDefault();

        Assert.Null(first);
    }

    [Fact]
    public void And_AddsAdditionalCondition()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20, IsActive = true });
        _table.Insert(new TestEntity { Name = "Test2", Age = 20, IsActive = false });
        _table.Insert(new TestEntity { Name = "Test3", Age = 30, IsActive = true });

        var results = _table.Query()
            .Where("Age", 20)
            .And("IsActive", true)
            .ToList();

        Assert.Single(results);
        Assert.Equal("Test1", results[0].Name);
    }

    [Fact]
    public void Or_AddsOrCondition()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20 });
        _table.Insert(new TestEntity { Name = "Test2", Age = 30 });
        _table.Insert(new TestEntity { Name = "Test3", Age = 40 });

        var results = _table.Query()
            .Where("Age", 20)
            .Or("Age", 40)
            .ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void MultipleWhereConditions_AppliesAll()
    {
        _table.CreateIfNotExists();
        _table.Insert(new TestEntity { Name = "Test1", Age = 20, IsActive = true });
        _table.Insert(new TestEntity { Name = "Test2", Age = 20, IsActive = false });
        _table.Insert(new TestEntity { Name = "Test3", Age = 30, IsActive = true });

        var results = _table.Query()
            .Where("Age", 20, ComparisonOperator.GreaterThan)
            .Where("IsActive", true)
            .ToList();

        Assert.Single(results);
        Assert.Equal("Test3", results[0].Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

using ovudb.Core;
using ovudb.OvuRequests;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.OvuRequests;

/// <summary>
/// Integration tests for full cycle: Parse -> Optimize -> Execute
/// </summary>
public class IntegrationTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;
    private readonly Table<TestEntity> _table;

    public IntegrationTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _database = new Database("testdb", dataDirectory: _testDataDirectory);
        _table = _database.GetTable<TestEntity>("TestTable");
        _table.AddColumn("Name", DataType.String)
              .AddColumn("Age", DataType.Integer)
              .AddColumn("IsActive", DataType.Boolean);
        _table.CreateIfNotExists();

        // Add test data
        _table.Insert(new TestEntity { Id = 1, Name = "John", Age = 25, IsActive = true });
        _table.Insert(new TestEntity { Id = 2, Name = "Jane", Age = 30, IsActive = true });
        _table.Insert(new TestEntity { Id = 3, Name = "Bob", Age = 20, IsActive = false });
        _table.Insert(new TestEntity { Id = 4, Name = "Alice", Age = 35, IsActive = true });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    [Fact]
    public void FullCycle_SimpleSelect_ReturnsResults()
    {
        // Parse
        var parser = new Parser("SELECT * FROM TestTable");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        Assert.True(resultDict.ContainsKey("rows"));
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void FullCycle_SelectWithWhere_ReturnsFilteredResults()
    {
        // Parse
        var parser = new Parser("SELECT * FROM TestTable WHERE age > 25");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(Convert.ToInt32(row["Age"]) > 25));
    }

    [Fact]
    public void FullCycle_SelectWithWhereAnd_ReturnsFilteredResults()
    {
        // Parse
        var parser = new Parser("SELECT * FROM TestTable WHERE age > 25 AND IsActive = TRUE");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.True(Convert.ToInt32(row["Age"]) > 25);
            Assert.True(Convert.ToBoolean(row["IsActive"]));
        });
    }

    [Fact]
    public void FullCycle_SelectWithOrderBy_ReturnsSortedResults()
    {
        // Parse
        var parser = new Parser("SELECT * FROM TestTable ORDER BY age");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(4, rows.Count);
        var ages = rows.Select(r => Convert.ToInt32(r["Age"])).ToList();
        Assert.Equal(ages.OrderBy(a => a), ages);
    }

    [Fact]
    public void FullCycle_SelectWithLimit_ReturnsLimitedResults()
    {
        // Parse
        var parser = new Parser("SELECT * FROM TestTable LIMIT 2");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void FullCycle_SelectWithLimitAndOffset_ReturnsCorrectResults()
    {
        // Parse
        var parser = new Parser("SELECT * FROM TestTable ORDER BY age LIMIT 2 OFFSET 1");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void FullCycle_SelectWithIn_ReturnsMatchingResults()
    {
        // Parse
        var parser = new Parser("SELECT * FROM TestTable WHERE age IN (25, 30)");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Contains(Convert.ToInt32(row["Age"]), new[] { 25, 30 }));
    }

    [Fact]
    public void FullCycle_SelectWithLike_ReturnsMatchingResults()
    {
        // Parse
        var parser = new Parser("SELECT * FROM TestTable WHERE name LIKE 'J%'");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.StartsWith("J", row["Name"]?.ToString() ?? ""));
    }

    [Fact]
    public void FullCycle_SelectWithSpecificColumns_ReturnsOnlySelectedColumns()
    {
        // Parse
        var parser = new Parser("SELECT name, age FROM TestTable");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.All(rows, row =>
        {
            Assert.True(row.ContainsKey("name"));
            Assert.True(row.ContainsKey("age"));
            Assert.False(row.ContainsKey("IsActive"));
        });
    }

    [Fact]
    public void FullCycle_SelectWithAggregate_ReturnsAggregateResult()
    {
        // Parse
        var parser = new Parser("SELECT COUNT(*) FROM TestTable");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Single(rows);
        Assert.True(rows[0].ContainsKey("Count(*)"));
        Assert.Equal(4, Convert.ToInt32(rows[0]["Count(*)"]));
    }

    [Fact]
    public void FullCycle_SelectWithGroupBy_ReturnsGroupedResults()
    {
        // Parse
        var parser = new Parser("SELECT IsActive, COUNT(*) FROM TestTable GROUP BY IsActive");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count); // true and false
    }

    [Fact]
    public void FullCycle_ComplexQuery_ReturnsCorrectResults()
    {
        // Parse complex query
        var parser = new Parser("SELECT name, age FROM TestTable WHERE age > 25 AND IsActive = TRUE ORDER BY age DESC LIMIT 2");
        var query = parser.Parse();

        // Optimize
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);

        // Execute
        var executor = new Executor(_database);
        var result = executor.Execute(optimizedQuery);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.True(Convert.ToInt32(row["age"]) > 25);
            // IsActive not in SELECT, so check only age
        });
        // Verify sorting
        var ages = rows.Select(r => Convert.ToInt32(r["age"])).ToList();
        Assert.Equal(ages.OrderByDescending(a => a), ages);
    }
}

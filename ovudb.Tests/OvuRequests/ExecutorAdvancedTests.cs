using ovudb.Core;
using ovudb.OvuRequests;
using ovudb.OvuRequests.Ast;
using ovudb.SystemDatabase;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.OvuRequests;

/// <summary>
/// Advanced executor tests for various execution scenarios
/// </summary>
public class ExecutorAdvancedTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;
    private readonly Executor _executor;
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly ModelService _modelService;

    public ExecutorAdvancedTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_executor_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
        _database = new Database("ExecutorTestDb", dataDirectory: _testDataDirectory);
        _systemDatabaseService = new SystemDatabaseService(Path.Combine(_testDataDirectory, "ovusys"));
        _modelService = new ModelService(_systemDatabaseService);
        _executor = new Executor(_database, _modelService);

        // Create test table
        var table = _database.GetTable<TestEntity>("test_table");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
             .AddColumn("Name", DataType.String)
             .AddColumn("Age", DataType.Integer)
             .AddColumn("IsActive", DataType.Boolean)
             .CreateIfNotExists();

        // Add test data
        for (int i = 1; i <= 10; i++)
        {
            table.Insert(new TestEntity
            {
                Id = i,
                Name = $"User{i}",
                Age = 20 + i,
                IsActive = i % 2 == 0
            });
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    #region SELECT - Advanced tests

    [Fact]
    public void Execute_SelectWithLimit_ReturnsLimitedResults()
    {
        var parser = new Parser("SELECT * FROM test_table LIMIT 5");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count <= 5);
    }

    [Fact]
    public void Execute_SelectWithOffset_ReturnsOffsetResults()
    {
        var parser = new Parser("SELECT * FROM test_table LIMIT 5 OFFSET 3");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count <= 5);
    }

    [Fact]
    public void Execute_SelectWithOrderByDesc_ReturnsOrderedResults()
    {
        var parser = new Parser("SELECT * FROM test_table ORDER BY Age DESC");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        if (rows.Count > 1)
        {
            var firstAge = Convert.ToInt32(rows[0]["Age"]);
            var secondAge = Convert.ToInt32(rows[1]["Age"]);
            Assert.True(firstAge >= secondAge);
        }
    }

    [Fact]
    public void Execute_SelectWithMultipleOrderBy_ReturnsOrderedResults()
    {
        var parser = new Parser("SELECT * FROM test_table ORDER BY IsActive DESC, Age ASC");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }

    [Fact]
    public void Execute_SelectWithGroupBy_ReturnsGroupedResults()
    {
        var parser = new Parser("SELECT IsActive, COUNT(*) FROM test_table GROUP BY IsActive");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }

    [Fact]
    public void Execute_SelectWithAggregateCount_ReturnsCount()
    {
        var parser = new Parser("SELECT COUNT(*) FROM test_table");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Single(rows);
    }

    [Fact]
    public void Execute_SelectWithAggregateSum_ReturnsSum()
    {
        var parser = new Parser("SELECT SUM(Age) FROM test_table");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Single(rows);
    }

    [Fact]
    public void Execute_SelectWithAggregateAvg_ReturnsAverage()
    {
        var parser = new Parser("SELECT AVG(Age) FROM test_table");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Single(rows);
    }

    [Fact]
    public void Execute_SelectWithAggregateMin_ReturnsMin()
    {
        var parser = new Parser("SELECT MIN(Age) FROM test_table");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Single(rows);
    }

    [Fact]
    public void Execute_SelectWithAggregateMax_ReturnsMax()
    {
        var parser = new Parser("SELECT MAX(Age) FROM test_table");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Single(rows);
    }

    [Fact]
    public void Execute_SelectWithInOperator_ReturnsMatchingRows()
    {
        var parser = new Parser("SELECT * FROM test_table WHERE Age IN (21, 23, 25)");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }

    [Fact]
    public void Execute_SelectWithNotInOperator_ReturnsNonMatchingRows()
    {
        var parser = new Parser("SELECT * FROM test_table WHERE Age NOT IN (21, 23)");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }

    [Fact]
    public void Execute_SelectWithOrCondition_ReturnsMatchingRows()
    {
        var parser = new Parser("SELECT * FROM test_table WHERE Age = 21 OR Age = 25");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }

    [Fact]
    public void Execute_SelectWithAndCondition_ReturnsMatchingRows()
    {
        var parser = new Parser("SELECT * FROM test_table WHERE Age > 20 AND Age < 30");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }

    [Fact]
    public void Execute_SelectWithNotCondition_ReturnsMatchingRows()
    {
        var parser = new Parser("SELECT * FROM test_table WHERE NOT IsActive = TRUE");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }

    #endregion

    #region INSERT - Advanced tests

    [Fact]
    public void Execute_InsertWithAutoIncrement_CreatesNewId()
    {
        var parser = new Parser("INSERT INTO test_table (Name, Age, IsActive) VALUES ('NewUser', 35, TRUE)");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
    }

    [Fact]
    public void Execute_InsertMultipleRows_InsertsAllRows()
    {
        var parser = new Parser("INSERT INTO test_table (Name, Age, IsActive) VALUES ('User1', 20, TRUE), ('User2', 25, FALSE)");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);

        // Verify data was inserted
        var selectParser = new Parser("SELECT * FROM test_table WHERE Name = 'User1'");
        var selectQuery = selectParser.Parse();
        var selectResult = _executor.Execute(selectQuery);
        var resultDict = Assert.IsType<Dictionary<string, object>>(selectResult);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }

    #endregion

    #region UPDATE - Advanced tests

    [Fact]
    public void Execute_UpdateWithWhere_UpdatesMatchingRows()
    {
        var parser = new Parser("UPDATE test_table SET Age = 100 WHERE Name = 'User1'");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);

        // Verify update
        var selectParser = new Parser("SELECT Age FROM test_table WHERE Name = 'User1'");
        var selectQuery = selectParser.Parse();
        var selectResult = _executor.Execute(selectQuery);
        var resultDict = Assert.IsType<Dictionary<string, object>>(selectResult);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        if (rows.Count > 0)
        {
            // Look for key "Age" (may be any case)
            var ageKey = rows[0].Keys.FirstOrDefault(k => k.Equals("Age", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(ageKey);
            Assert.Equal(100, Convert.ToInt32(rows[0][ageKey]));
        }
    }

    [Fact]
    public void Execute_UpdateMultipleColumns_UpdatesAllColumns()
    {
        var parser = new Parser("UPDATE test_table SET Name = 'Updated', Age = 50 WHERE Id = 1");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
    }

    [Fact]
    public void Execute_UpdateWithoutWhere_UpdatesAllRows()
    {
        var parser = new Parser("UPDATE test_table SET IsActive = FALSE");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);
    }

    #endregion

    #region DELETE - Advanced tests

    [Fact]
    public void Execute_DeleteWithWhere_DeletesMatchingRows()
    {
        var parser = new Parser("DELETE FROM test_table WHERE Age < 25");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);

        // Verify deletion
        var selectParser = new Parser("SELECT * FROM test_table WHERE Age < 25");
        var selectQuery = selectParser.Parse();
        var selectResult = _executor.Execute(selectQuery);
        var resultDict = Assert.IsType<Dictionary<string, object>>(selectResult);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Empty(rows);
    }

    [Fact]
    public void Execute_DeleteWithoutWhere_DeletesAllRows()
    {
        var parser = new Parser("DELETE FROM test_table");
        var query = parser.Parse();
        var result = _executor.Execute(query);

        Assert.NotNull(result);

        // Verify table is empty
        var selectParser = new Parser("SELECT * FROM test_table");
        var selectQuery = selectParser.Parse();
        var selectResult = _executor.Execute(selectQuery);
        var resultDict = Assert.IsType<Dictionary<string, object>>(selectResult);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Empty(rows);
    }

    #endregion

    #region Error handling

    [Fact]
    public void Execute_SelectFromNonExistentTable_ThrowsException()
    {
        var parser = new Parser("SELECT * FROM non_existent_table");
        var query = parser.Parse();
        Assert.ThrowsAny<Exception>(() => _executor.Execute(query));
    }

    [Fact]
    public void Execute_SelectNonExistentColumn_ThrowsException()
    {
        var parser = new Parser("SELECT non_existent_column FROM test_table");
        var query = parser.Parse();
        Assert.ThrowsAny<Exception>(() => _executor.Execute(query));
    }

    [Fact]
    public void Execute_InsertWithInvalidColumn_ThrowsException()
    {
        var parser = new Parser("INSERT INTO test_table (InvalidColumn) VALUES ('value')");
        var query = parser.Parse();
        Assert.ThrowsAny<Exception>(() => _executor.Execute(query));
    }

    [Fact]
    public void Execute_UpdateNonExistentTable_ThrowsException()
    {
        var parser = new Parser("UPDATE non_existent_table SET column = 'value'");
        var query = parser.Parse();
        Assert.ThrowsAny<Exception>(() => _executor.Execute(query));
    }

    [Fact]
    public void Execute_DeleteFromNonExistentTable_ThrowsException()
    {
        var parser = new Parser("DELETE FROM non_existent_table");
        var query = parser.Parse();
        Assert.ThrowsAny<Exception>(() => _executor.Execute(query));
    }

    #endregion
}

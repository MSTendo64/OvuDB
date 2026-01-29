using ovudb.Core;
using ovudb.OvuRequests;
using ovudb.OvuRequests.Ast;
using ovudb.Query;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.OvuRequests;

public class ExecutorTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;
    private readonly Table<TestEntity> _table;

    public ExecutorTests()
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
    public void Execute_SimpleSelect_ReturnsAllRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        Assert.NotNull(result);
        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        Assert.True(resultDict.ContainsKey("rows"));
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void Execute_SelectWithWhere_ReturnsFilteredRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "Age",
                    Operator = ComparisonOperator.GreaterThan,
                    Value = 25
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(Convert.ToInt32(row["Age"]) > 25));
    }

    [Fact]
    public void Execute_SelectWithWhereEquals_ReturnsMatchingRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "Name",
                    Operator = ComparisonOperator.Equals,
                    Value = "John"
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Single(rows);
        Assert.Equal("John", rows[0]["Name"]);
    }

    [Fact]
    public void Execute_SelectWithWhereAnd_ReturnsMatchingRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    Left = new ConditionNode
                    {
                        ColumnName = "Age",
                        Operator = ComparisonOperator.GreaterThan,
                        Value = 25
                    },
                    Right = new ConditionNode
                    {
                        ColumnName = "IsActive",
                        Operator = ComparisonOperator.Equals,
                        Value = true
                    },
                    LogicalOp = LogicalOperator.And
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

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
    public void Execute_SelectWithWhereOr_ReturnsMatchingRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    Left = new ConditionNode
                    {
                        ColumnName = "Name",
                        Operator = ComparisonOperator.Equals,
                        Value = "John"
                    },
                    Right = new ConditionNode
                    {
                        ColumnName = "Name",
                        Operator = ComparisonOperator.Equals,
                        Value = "Jane"
                    },
                    LogicalOp = LogicalOperator.Or
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r["Name"]?.ToString() == "John");
        Assert.Contains(rows, r => r["Name"]?.ToString() == "Jane");
    }

    [Fact]
    public void Execute_SelectWithOrderBy_ReturnsSortedRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            OrderBy = new OrderByNode
            {
                Items = new List<OrderByItem>
                {
                    new OrderByItem { ColumnName = "Age", Descending = false }
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(4, rows.Count);
        var ages = rows.Select(r => Convert.ToInt32(r["Age"])).ToList();
        Assert.Equal(ages.OrderBy(a => a), ages);
    }

    [Fact]
    public void Execute_SelectWithOrderByDesc_ReturnsDescendingSortedRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            OrderBy = new OrderByNode
            {
                Items = new List<OrderByItem>
                {
                    new OrderByItem { ColumnName = "Age", Descending = true }
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        var ages = rows.Select(r => Convert.ToInt32(r["Age"])).ToList();
        Assert.Equal(ages.OrderByDescending(a => a), ages);
    }

    [Fact]
    public void Execute_SelectWithLimit_ReturnsLimitedRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Limit = new LimitNode { Count = 2 }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Execute_SelectWithOffset_ReturnsOffsetRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Offset = new OffsetNode { Count = 2 }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Execute_SelectWithLimitAndOffset_ReturnsCorrectRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            OrderBy = new OrderByNode
            {
                Items = new List<OrderByItem>
                {
                    new OrderByItem { ColumnName = "Age", Descending = false }
                }
            },
            Limit = new LimitNode { Count = 2 },
            Offset = new OffsetNode { Count = 1 }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Execute_SelectWithSpecificColumns_ReturnsOnlySelectedColumns()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "Name", "Age" }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.All(rows, row =>
        {
            Assert.True(row.ContainsKey("Name"));
            Assert.True(row.ContainsKey("Age"));
            Assert.False(row.ContainsKey("IsActive"));
        });
    }

    [Fact]
    public void Execute_SelectWithLike_ReturnsMatchingRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "Name",
                    Operator = ComparisonOperator.Like,
                    Value = "J"
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Contains("J", row["Name"]?.ToString() ?? ""));
    }

    [Fact]
    public void Execute_SelectWithIn_ReturnsMatchingRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "Age",
                    Operator = ComparisonOperator.In,
                    Values = new List<object?> { 25, 30 }
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Contains(Convert.ToInt32(row["Age"]), new[] { 25, 30 }));
    }

    [Fact]
    public void Execute_SelectWithNotIn_ReturnsNonMatchingRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "Age",
                    Operator = ComparisonOperator.NotIn,
                    Values = new List<object?> { 25, 30 }
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.DoesNotContain(Convert.ToInt32(row["Age"]), new[] { 25, 30 }));
    }

    [Fact]
    public void Execute_SelectWithComparisonOperators_ReturnsCorrectRows()
    {
        var operators = new[]
        {
            (ComparisonOperator.GreaterThan, 25, 2),
            (ComparisonOperator.GreaterThanOrEqual, 25, 3),
            (ComparisonOperator.LessThan, 30, 2),
            (ComparisonOperator.LessThanOrEqual, 30, 3),
            (ComparisonOperator.NotEquals, 25, 3)
        };

        foreach (var (op, value, expectedCount) in operators)
        {
            var query = new SelectNode
            {
                TableName = "TestTable",
                Columns = new List<string> { "*" },
                Where = new WhereNode
                {
                    Condition = new ConditionNode
                    {
                        ColumnName = "Age",
                        Operator = op,
                        Value = value
                    }
                }
            };

            var executor = new Executor(_database);
            var result = executor.Execute(query);

            var resultDict = Assert.IsType<Dictionary<string, object>>(result);
            var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
            Assert.Equal(expectedCount, rows.Count);
        }
    }

    [Fact]
    public void Execute_SelectWithGroupBy_ReturnsGroupedRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "IsActive" },
            GroupBy = new GroupByNode
            {
                Columns = new List<string> { "IsActive" }
            },
            AggregateFunctions = new List<AggregateFunction>
            {
                new AggregateFunction { Type = AggregateType.Count, ColumnName = "*" }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Equal(2, rows.Count); // true and false
    }

    [Fact]
    public void Execute_SelectWithAggregateCount_ReturnsCount()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            AggregateFunctions = new List<AggregateFunction>
            {
                new AggregateFunction { Type = AggregateType.Count, ColumnName = "*" }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Single(rows);
        Assert.True(rows[0].ContainsKey("Count(*)"));
    }

    [Fact]
    public void Execute_SelectWithAggregateSum_ReturnsSum()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            AggregateFunctions = new List<AggregateFunction>
            {
                new AggregateFunction { Type = AggregateType.Sum, ColumnName = "Age" }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.Single(rows);
        Assert.True(rows[0].ContainsKey("Sum(Age)"));
    }

    [Fact]
    public void Execute_SelectNonExistentTable_ThrowsException()
    {
        var query = new SelectNode
        {
            TableName = "NonExistentTable",
            Columns = new List<string> { "*" }
        };

        var executor = new Executor(_database);
        Assert.Throws<InvalidOperationException>(() => executor.Execute(query));
    }

    [Fact]
    public void Execute_SelectWithComplexWhere_ReturnsCorrectRows()
    {
        var query = new SelectNode
        {
            TableName = "TestTable",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    Left = new ConditionNode
                    {
                        ColumnName = "Age",
                        Operator = ComparisonOperator.GreaterThan,
                        Value = 25
                    },
                    Right = new ConditionNode
                    {
                        Left = new ConditionNode
                        {
                            ColumnName = "IsActive",
                            Operator = ComparisonOperator.Equals,
                            Value = true
                        },
                        Right = new ConditionNode
                        {
                            ColumnName = "Name",
                            Operator = ComparisonOperator.Like,
                            Value = "J"
                        },
                        LogicalOp = LogicalOperator.Or
                    },
                    LogicalOp = LogicalOperator.And
                }
            }
        };

        var executor = new Executor(_database);
        var result = executor.Execute(query);

        var resultDict = Assert.IsType<Dictionary<string, object>>(result);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(resultDict["rows"]);
        Assert.True(rows.Count > 0);
    }
}

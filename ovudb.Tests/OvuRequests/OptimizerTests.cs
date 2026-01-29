using ovudb.Core;
using ovudb.OvuRequests;
using ovudb.OvuRequests.Ast;
using ovudb.Query;
using ovudb.Storage;
using Xunit;

namespace ovudb.Tests.OvuRequests;

public class OptimizerTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;

    public OptimizerTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _database = new Database("testdb", dataDirectory: _testDataDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    [Fact]
    public void Optimize_SelectQuery_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        Assert.IsType<SelectNode>(result);
        var select = (SelectNode)result;
        Assert.Equal("users", select.TableName);
    }

    [Fact]
    public void Optimize_SelectWithWhere_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "age",
                    Operator = ComparisonOperator.GreaterThan,
                    Value = 18
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Optimize_SelectWithAndCondition_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    Left = new ConditionNode
                    {
                        ColumnName = "age",
                        Operator = ComparisonOperator.GreaterThan,
                        Value = 18
                    },
                    Right = new ConditionNode
                    {
                        ColumnName = "status",
                        Operator = ComparisonOperator.Equals,
                        Value = "active"
                    },
                    LogicalOp = LogicalOperator.And
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.NotNull(select.Where.Condition.Left);
        Assert.NotNull(select.Where.Condition.Right);
    }

    [Fact]
    public void Optimize_SelectWithOrCondition_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    Left = new ConditionNode
                    {
                        ColumnName = "age",
                        Operator = ComparisonOperator.LessThan,
                        Value = 18
                    },
                    Right = new ConditionNode
                    {
                        ColumnName = "age",
                        Operator = ComparisonOperator.GreaterThan,
                        Value = 65
                    },
                    LogicalOp = LogicalOperator.Or
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.Equal(LogicalOperator.Or, select.Where.Condition.LogicalOp);
    }

    [Fact]
    public void Optimize_SelectWithOrderBy_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            OrderBy = new OrderByNode
            {
                Items = new List<OrderByItem>
                {
                    new OrderByItem { ColumnName = "name", Descending = false }
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.OrderBy);
    }

    [Fact]
    public void Optimize_UpdateQuery_ReturnsOptimizedQuery()
    {
        var query = new UpdateNode
        {
            TableName = "users",
            SetValues = new Dictionary<string, object?> { { "status", "inactive" } },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "id",
                    Operator = ComparisonOperator.Equals,
                    Value = 123
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var update = Assert.IsType<UpdateNode>(result);
        Assert.NotNull(update.Where);
    }

    [Fact]
    public void Optimize_DeleteQuery_ReturnsOptimizedQuery()
    {
        var query = new DeleteNode
        {
            TableName = "users",
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "id",
                    Operator = ComparisonOperator.Equals,
                    Value = 123
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var delete = Assert.IsType<DeleteNode>(result);
        Assert.NotNull(delete.Where);
    }

    [Fact]
    public void Optimize_InsertQuery_ReturnsSameQuery()
    {
        var query = new InsertNode
        {
            TableName = "users",
            Columns = new List<string> { "name", "email" },
            Values = new List<List<object?>>
            {
                new List<object?> { "John", "john@example.com" }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Equal("users", insert.TableName);
    }

    [Fact]
    public void Optimize_SelectWithComplexWhere_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    Left = new ConditionNode
                    {
                        ColumnName = "age",
                        Operator = ComparisonOperator.GreaterThan,
                        Value = 18
                    },
                    Right = new ConditionNode
                    {
                        Left = new ConditionNode
                        {
                            ColumnName = "status",
                            Operator = ComparisonOperator.Equals,
                            Value = "active"
                        },
                        Right = new ConditionNode
                        {
                            ColumnName = "role",
                            Operator = ComparisonOperator.Equals,
                            Value = "admin"
                        },
                        LogicalOp = LogicalOperator.Or
                    },
                    LogicalOp = LogicalOperator.And
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        // Verify structure preserved
        Assert.NotNull(select.Where.Condition.Left);
        Assert.NotNull(select.Where.Condition.Right);
    }

    [Fact]
    public void Optimize_SelectWithInCondition_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "id",
                    Operator = ComparisonOperator.In,
                    Values = new List<object?> { 1, 2, 3 }
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.Equal(ComparisonOperator.In, select.Where.Condition.Operator);
    }

    [Fact]
    public void Optimize_SelectWithLikeCondition_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "name",
                    Operator = ComparisonOperator.Like,
                    Value = "%john%"
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.Equal(ComparisonOperator.Like, select.Where.Condition.Operator);
    }

    [Fact]
    public void Optimize_SelectWithNegatedCondition_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Where = new WhereNode
            {
                Condition = new ConditionNode
                {
                    ColumnName = "age",
                    Operator = ComparisonOperator.GreaterThan,
                    Value = 18,
                    IsNegated = true
                }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.True(select.Where.Condition.IsNegated);
    }

    [Fact]
    public void Optimize_SelectWithGroupBy_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "products",
            Columns = new List<string> { "category" },
            GroupBy = new GroupByNode
            {
                Columns = new List<string> { "category" }
            },
            AggregateFunctions = new List<AggregateFunction>
            {
                new AggregateFunction { Type = AggregateType.Count, ColumnName = "*" }
            }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.GroupBy);
    }

    [Fact]
    public void Optimize_SelectWithLimitAndOffset_ReturnsOptimizedQuery()
    {
        var query = new SelectNode
        {
            TableName = "users",
            Columns = new List<string> { "*" },
            Limit = new LimitNode { Count = 10 },
            Offset = new OffsetNode { Count = 5 }
        };

        var optimizer = new Optimizer(_database);
        var result = optimizer.Optimize(query);

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Limit);
        Assert.NotNull(select.Offset);
        Assert.Equal(10, select.Limit.Count);
        Assert.Equal(5, select.Offset.Count);
    }
}

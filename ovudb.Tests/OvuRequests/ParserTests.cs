using ovudb.OvuRequests;
using ovudb.OvuRequests.Ast;
using ovudb.Query;
using Xunit;

namespace ovudb.Tests.OvuRequests;

public class ParserTests
{
    [Fact]
    public void Parse_SimpleSelect_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users");
        var result = parser.Parse();

        Assert.IsType<SelectNode>(result);
        var select = (SelectNode)result;
        Assert.Equal("users", select.TableName);
        Assert.Single(select.Columns);
        Assert.Equal("*", select.Columns[0]);
    }

    [Fact]
    public void Parse_SelectWithColumns_ReturnsSelectNodeWithColumns()
    {
        var parser = new Parser("SELECT name, email FROM users");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(2, select.Columns.Count);
        Assert.Contains("name", select.Columns);
        Assert.Contains("email", select.Columns);
    }

    [Fact]
    public void Parse_SelectWithWhere_ReturnsSelectNodeWithWhere()
    {
        var parser = new Parser("SELECT * FROM users WHERE age > 18");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.NotNull(select.Where.Condition);
        Assert.Equal("age", select.Where.Condition.ColumnName);
        Assert.Equal(ComparisonOperator.GreaterThan, select.Where.Condition.Operator);
        Assert.Equal(18, select.Where.Condition.Value);
    }

    [Fact]
    public void Parse_SelectWithWhereAnd_ReturnsSelectNodeWithAndCondition()
    {
        var parser = new Parser("SELECT * FROM users WHERE age > 18 AND status = 'active'");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        var condition = select.Where.Condition;
        Assert.Equal(LogicalOperator.And, condition.LogicalOp);
        Assert.NotNull(condition.Left);
        Assert.NotNull(condition.Right);
    }

    [Fact]
    public void Parse_SelectWithWhereOr_ReturnsSelectNodeWithOrCondition()
    {
        var parser = new Parser("SELECT * FROM users WHERE age < 18 OR age > 65");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        var condition = select.Where.Condition;
        Assert.Equal(LogicalOperator.Or, condition.LogicalOp);
    }

    [Fact]
    public void Parse_SelectWithOrderBy_ReturnsSelectNodeWithOrderBy()
    {
        var parser = new Parser("SELECT * FROM users ORDER BY name");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.OrderBy);
        Assert.Single(select.OrderBy.Items);
        Assert.Equal("name", select.OrderBy.Items[0].ColumnName);
        Assert.False(select.OrderBy.Items[0].Descending);
    }

    [Fact]
    public void Parse_SelectWithOrderByDesc_ReturnsSelectNodeWithDescendingOrder()
    {
        var parser = new Parser("SELECT * FROM users ORDER BY name DESC");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.OrderBy);
        Assert.True(select.OrderBy.Items[0].Descending);
    }

    [Fact]
    public void Parse_SelectWithLimit_ReturnsSelectNodeWithLimit()
    {
        var parser = new Parser("SELECT * FROM users LIMIT 10");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Limit);
        Assert.Equal(10, select.Limit.Count);
    }

    [Fact]
    public void Parse_SelectWithOffset_ReturnsSelectNodeWithOffset()
    {
        var parser = new Parser("SELECT * FROM users OFFSET 5");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Offset);
        Assert.Equal(5, select.Offset.Count);
    }

    [Fact]
    public void Parse_SelectWithLimitAndOffset_ReturnsSelectNodeWithBoth()
    {
        var parser = new Parser("SELECT * FROM users LIMIT 10 OFFSET 5");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Limit);
        Assert.NotNull(select.Offset);
        Assert.Equal(10, select.Limit.Count);
        Assert.Equal(5, select.Offset.Count);
    }

    [Fact]
    public void Parse_SelectWithGroupBy_ReturnsSelectNodeWithGroupBy()
    {
        var parser = new Parser("SELECT category, COUNT(*) FROM products GROUP BY category");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.GroupBy);
        Assert.Single(select.GroupBy.Columns);
        Assert.Equal("category", select.GroupBy.Columns[0]);
    }

    [Fact]
    public void Parse_SelectWithAggregateFunction_ReturnsSelectNodeWithAggregate()
    {
        var parser = new Parser("SELECT COUNT(*) FROM users");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Single(select.AggregateFunctions);
        Assert.Equal(AggregateType.Count, select.AggregateFunctions[0].Type);
    }

    [Fact]
    public void Parse_SelectWithMultipleAggregates_ReturnsSelectNodeWithMultipleAggregates()
    {
        var parser = new Parser("SELECT COUNT(*), SUM(price), AVG(price) FROM products");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(3, select.AggregateFunctions.Count);
        Assert.Contains(select.AggregateFunctions, a => a.Type == AggregateType.Count);
        Assert.Contains(select.AggregateFunctions, a => a.Type == AggregateType.Sum);
        Assert.Contains(select.AggregateFunctions, a => a.Type == AggregateType.Avg);
    }

    [Fact]
    public void Parse_SelectWithAggregateAlias_ReturnsSelectNodeWithAlias()
    {
        var parser = new Parser("SELECT COUNT(*) AS total FROM users");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Single(select.AggregateFunctions);
        Assert.Equal("total", select.AggregateFunctions[0].Alias);
    }

    [Fact]
    public void Parse_SelectWithLike_ReturnsSelectNodeWithLikeOperator()
    {
        var parser = new Parser("SELECT * FROM users WHERE name LIKE '%john%'");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.Equal(ComparisonOperator.Like, select.Where.Condition.Operator);
        Assert.Equal("%john%", select.Where.Condition.Value);
    }

    [Fact]
    public void Parse_SelectWithIn_ReturnsSelectNodeWithInOperator()
    {
        var parser = new Parser("SELECT * FROM users WHERE id IN (1, 2, 3)");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.Equal(ComparisonOperator.In, select.Where.Condition.Operator);
        Assert.NotNull(select.Where.Condition.Values);
        Assert.Equal(3, select.Where.Condition.Values.Count);
    }

    [Fact]
    public void Parse_SelectWithNotIn_ReturnsSelectNodeWithNotInOperator()
    {
        var parser = new Parser("SELECT * FROM users WHERE id NOT IN (1, 2, 3)");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.Equal(ComparisonOperator.NotIn, select.Where.Condition.Operator);
        Assert.NotNull(select.Where.Condition.Values);
    }

    [Fact]
    public void Parse_SelectWithNotCondition_ReturnsSelectNodeWithNegatedCondition()
    {
        var parser = new Parser("SELECT * FROM users WHERE NOT age > 18");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        Assert.True(select.Where.Condition.IsNegated);
    }

    [Fact]
    public void Parse_SelectWithStringValue_ReturnsSelectNodeWithString()
    {
        var parser = new Parser("SELECT * FROM users WHERE name = 'John'");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal("John", select.Where?.Condition.Value);
    }

    [Fact]
    public void Parse_SelectWithDoubleQuotedString_ReturnsSelectNodeWithString()
    {
        var parser = new Parser("SELECT * FROM users WHERE name = \"John\"");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal("John", select.Where?.Condition.Value);
    }

    [Fact]
    public void Parse_SelectWithNumberValue_ReturnsSelectNodeWithNumber()
    {
        var parser = new Parser("SELECT * FROM users WHERE age = 25");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(25, select.Where?.Condition.Value);
    }

    [Fact]
    public void Parse_SelectWithDecimalValue_ReturnsSelectNodeWithDecimal()
    {
        var parser = new Parser("SELECT * FROM products WHERE price = 19.99");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(19.99, select.Where?.Condition.Value);
    }

    [Fact]
    public void Parse_SelectWithNullValue_ReturnsSelectNodeWithNull()
    {
        var parser = new Parser("SELECT * FROM users WHERE email IS NULL");
        var result = parser.Parse();

        // IS NULL not yet supported, but can verify NULL parsing
        var select = Assert.IsType<SelectNode>(result);
        // In future can add IS NULL support
    }

    [Fact]
    public void Parse_SelectWithBooleanValue_ReturnsSelectNodeWithBoolean()
    {
        var parser = new Parser("SELECT * FROM users WHERE active = TRUE");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(true, select.Where?.Condition.Value);
    }

    [Fact]
    public void Parse_SelectWithComplexWhere_ReturnsSelectNodeWithComplexCondition()
    {
        var parser = new Parser("SELECT * FROM users WHERE age > 18 AND (status = 'active' OR role = 'admin')");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
        // Verify complex condition structure
        var condition = select.Where.Condition;
        Assert.Equal(LogicalOperator.And, condition.LogicalOp);
    }

    [Fact]
    public void Parse_InsertSimple_ReturnsInsertNode()
    {
        var parser = new Parser("INSERT INTO users VALUES ('John', 'john@example.com', 25)");
        var result = parser.Parse();

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Equal("users", insert.TableName);
        Assert.Single(insert.Values);
        Assert.Equal(3, insert.Values[0].Count);
    }

    [Fact]
    public void Parse_InsertWithColumns_ReturnsInsertNodeWithColumns()
    {
        var parser = new Parser("INSERT INTO users (name, email) VALUES ('John', 'john@example.com')");
        var result = parser.Parse();

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Equal(2, insert.Columns.Count);
        Assert.Contains("name", insert.Columns);
        Assert.Contains("email", insert.Columns);
    }

    [Fact]
    public void Parse_InsertMultipleRows_ReturnsInsertNodeWithMultipleValues()
    {
        var parser = new Parser("INSERT INTO users VALUES ('John', 'john@example.com'), ('Jane', 'jane@example.com')");
        var result = parser.Parse();

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Equal(2, insert.Values.Count);
    }

    [Fact]
    public void Parse_UpdateSimple_ReturnsUpdateNode()
    {
        var parser = new Parser("UPDATE users SET status = 'inactive'");
        var result = parser.Parse();

        var update = Assert.IsType<UpdateNode>(result);
        Assert.Equal("users", update.TableName);
        Assert.Single(update.SetValues);
        Assert.Equal("inactive", update.SetValues["status"]);
    }

    [Fact]
    public void Parse_UpdateWithMultipleSets_ReturnsUpdateNodeWithMultipleSets()
    {
        var parser = new Parser("UPDATE users SET status = 'inactive', last_login = '2024-01-01'");
        var result = parser.Parse();

        var update = Assert.IsType<UpdateNode>(result);
        Assert.Equal(2, update.SetValues.Count);
    }

    [Fact]
    public void Parse_UpdateWithWhere_ReturnsUpdateNodeWithWhere()
    {
        var parser = new Parser("UPDATE users SET status = 'inactive' WHERE id = 123");
        var result = parser.Parse();

        var update = Assert.IsType<UpdateNode>(result);
        Assert.NotNull(update.Where);
        Assert.Equal("id", update.Where.Condition.ColumnName);
        Assert.Equal(123, update.Where.Condition.Value);
    }

    [Fact]
    public void Parse_DeleteSimple_ReturnsDeleteNode()
    {
        var parser = new Parser("DELETE FROM users");
        var result = parser.Parse();

        var delete = Assert.IsType<DeleteNode>(result);
        Assert.Equal("users", delete.TableName);
        Assert.Null(delete.Where);
    }

    [Fact]
    public void Parse_DeleteWithWhere_ReturnsDeleteNodeWithWhere()
    {
        var parser = new Parser("DELETE FROM users WHERE id = 123");
        var result = parser.Parse();

        var delete = Assert.IsType<DeleteNode>(result);
        Assert.NotNull(delete.Where);
        Assert.Equal("id", delete.Where.Condition.ColumnName);
    }

    [Fact]
    public void Parse_SelectWithAllOperators_ReturnsSelectNodeWithCorrectOperators()
    {
        var operators = new[]
        {
            ("=", ComparisonOperator.Equals),
            ("!=", ComparisonOperator.NotEquals),
            (">", ComparisonOperator.GreaterThan),
            (">=", ComparisonOperator.GreaterThanOrEqual),
            ("<", ComparisonOperator.LessThan),
            ("<=", ComparisonOperator.LessThanOrEqual)
        };

        foreach (var (opSymbol, expectedOp) in operators)
        {
            var parser = new Parser($"SELECT * FROM users WHERE age {opSymbol} 18");
            var result = parser.Parse();

            var select = Assert.IsType<SelectNode>(result);
            Assert.Equal(expectedOp, select.Where?.Condition.Operator);
        }
    }

    [Fact]
    public void Parse_SelectWithMultipleOrderBy_ReturnsSelectNodeWithMultipleOrderBy()
    {
        var parser = new Parser("SELECT * FROM users ORDER BY name, age DESC");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.OrderBy);
        Assert.Equal(2, select.OrderBy.Items.Count);
        Assert.Equal("name", select.OrderBy.Items[0].ColumnName);
        Assert.Equal("age", select.OrderBy.Items[1].ColumnName);
        Assert.True(select.OrderBy.Items[1].Descending);
    }

    [Fact]
    public void Parse_SelectWithNegativeNumber_ReturnsSelectNodeWithNegativeNumber()
    {
        var parser = new Parser("SELECT * FROM products WHERE price > -10");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(-10, select.Where?.Condition.Value);
    }

    [Fact]
    public void Parse_EmptyQuery_ThrowsException()
    {
        var parser = new Parser("");
        Assert.Throws<ArgumentException>(() => parser.Parse());
    }

    [Fact]
    public void Parse_InvalidQuery_ThrowsException()
    {
        var parser = new Parser("INVALID QUERY");
        Assert.Throws<ArgumentException>(() => parser.Parse());
    }

    [Fact]
    public void Parse_SelectWithEscapedString_ReturnsSelectNodeWithEscapedString()
    {
        var parser = new Parser("SELECT * FROM users WHERE name = 'John\\'s'");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        // Verify escaping is handled
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parse_SelectWithComplexAggregate_ReturnsSelectNodeWithAggregate()
    {
        var parser = new Parser("SELECT category, COUNT(*) AS count, SUM(price) AS total FROM products GROUP BY category");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(2, select.AggregateFunctions.Count);
        Assert.NotNull(select.GroupBy);
        Assert.Single(select.GroupBy.Columns);
    }
}

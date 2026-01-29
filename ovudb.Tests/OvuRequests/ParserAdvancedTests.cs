using ovudb.OvuRequests;
using ovudb.OvuRequests.Ast;
using Xunit;

namespace ovudb.Tests.OvuRequests;

/// <summary>
/// Advanced parser tests for edge cases and complex scenarios
/// </summary>
public class ParserAdvancedTests
{
    #region SELECT - Advanced tests

    [Fact]
    public void Parse_SelectWithMultipleAggregates_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT COUNT(*), SUM(price), AVG(age) FROM products");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(3, select.AggregateFunctions.Count);
    }

    [Fact]
    public void Parse_SelectWithNestedConditions_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users WHERE age > 18 AND (name LIKE 'J%' OR email LIKE '%@gmail.com')");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parse_SelectWithOrderByMultipleColumns_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users ORDER BY age DESC, name ASC");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.OrderBy);
        Assert.Equal(2, select.OrderBy.Items.Count);
    }

    [Fact]
    public void Parse_SelectWithGroupBy_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT category, COUNT(*) FROM products GROUP BY category");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.GroupBy);
        Assert.Single(select.GroupBy.Columns);
    }

    [Fact]
    public void Parse_SelectWithLimitAndOffset_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users LIMIT 10 OFFSET 20");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Limit);
        Assert.Equal(10, select.Limit.Count);
        Assert.NotNull(select.Offset);
        Assert.Equal(20, select.Offset.Count);
    }

    [Fact]
    public void Parse_SelectWithAlias_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT name AS user_name, age AS user_age FROM users");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.Equal(2, select.Columns.Count);
    }

    [Fact]
    public void Parse_SelectWithInOperator_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users WHERE age IN (18, 25, 30)");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parse_SelectWithNotInOperator_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users WHERE age NOT IN (18, 25)");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parse_SelectWithNullCheck_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users WHERE email IS NULL");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parse_SelectWithNotNullCheck_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users WHERE email IS NOT NULL");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    #endregion

    #region INSERT - Advanced tests

    [Fact]
    public void Parse_InsertWithMultipleRows_ReturnsInsertNode()
    {
        var parser = new Parser("INSERT INTO users VALUES (1, 'John', 25), (2, 'Jane', 30)");
        var result = parser.Parse();

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Equal(2, insert.Values.Count);
    }

    [Fact]
    public void Parse_InsertWithSpecifiedColumns_ReturnsInsertNode()
    {
        var parser = new Parser("INSERT INTO users (name, age) VALUES ('John', 25)");
        var result = parser.Parse();

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Equal(2, insert.Columns.Count);
        Assert.Single(insert.Values);
    }

    [Fact]
    public void Parse_InsertWithNullValues_ReturnsInsertNode()
    {
        var parser = new Parser("INSERT INTO users (name, email) VALUES ('John', NULL)");
        var result = parser.Parse();

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Single(insert.Values);
    }

    [Fact]
    public void Parse_InsertWithBooleanValues_ReturnsInsertNode()
    {
        var parser = new Parser("INSERT INTO users (name, is_active) VALUES ('John', TRUE)");
        var result = parser.Parse();

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Single(insert.Values);
    }

    [Fact]
    public void Parse_InsertWithStringEscaping_ReturnsInsertNode()
    {
        var parser = new Parser("INSERT INTO users (name) VALUES ('John''s name')");
        var result = parser.Parse();

        var insert = Assert.IsType<InsertNode>(result);
        Assert.Single(insert.Values);
    }

    #endregion

    #region UPDATE - Advanced tests

    [Fact]
    public void Parse_UpdateWithMultipleColumns_ReturnsUpdateNode()
    {
        var parser = new Parser("UPDATE users SET name = 'John', age = 25 WHERE id = 1");
        var result = parser.Parse();

        var update = Assert.IsType<UpdateNode>(result);
        Assert.Equal(2, update.SetValues.Count);
    }

    [Fact]
    public void Parse_UpdateWithoutWhere_ReturnsUpdateNode()
    {
        var parser = new Parser("UPDATE users SET status = 'inactive'");
        var result = parser.Parse();

        var update = Assert.IsType<UpdateNode>(result);
        Assert.Null(update.Where);
    }

    [Fact]
    public void Parse_UpdateWithComplexWhere_ReturnsUpdateNode()
    {
        var parser = new Parser("UPDATE users SET age = age + 1 WHERE age < 18 AND status = 'active'");
        var result = parser.Parse();

        var update = Assert.IsType<UpdateNode>(result);
        Assert.NotNull(update.Where);
    }

    #endregion

    #region DELETE - Advanced tests

    [Fact]
    public void Parse_DeleteWithoutWhere_ReturnsDeleteNode()
    {
        var parser = new Parser("DELETE FROM users");
        var result = parser.Parse();

        var delete = Assert.IsType<DeleteNode>(result);
        Assert.Null(delete.Where);
    }

    [Fact]
    public void Parse_DeleteWithComplexWhere_ReturnsDeleteNode()
    {
        var parser = new Parser("DELETE FROM users WHERE age < 18 OR status = 'inactive'");
        var result = parser.Parse();

        var delete = Assert.IsType<DeleteNode>(result);
        Assert.NotNull(delete.Where);
    }

    #endregion

    #region CREATE TABLE - Advanced tests

    [Fact]
    public void Parse_CreateTableWithAllConstraints_ReturnsCreateTableNode()
    {
        var parser = new Parser("CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name STRING NOT NULL UNIQUE, email STRING)");
        var result = parser.Parse();

        var create = Assert.IsType<CreateTableNode>(result);
        Assert.Equal(3, create.Columns.Count);
    }

    [Fact]
    public void Parse_CreateTableWithDefaultValue_ReturnsCreateTableNode()
    {
        var parser = new Parser("CREATE TABLE users (id INTEGER, status STRING DEFAULT 'active')");
        var result = parser.Parse();

        var create = Assert.IsType<CreateTableNode>(result);
        Assert.Equal(2, create.Columns.Count);
    }

    [Fact]
    public void Parse_CreateTableWithMultiplePrimaryKeys_ThrowsException()
    {
        var parser = new Parser("CREATE TABLE users (id1 INTEGER PRIMARY KEY, id2 INTEGER PRIMARY KEY)");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    #endregion

    #region MODEL - Advanced tests

    [Fact]
    public void Parse_ModelAddWithComplexFields_ReturnsModelAddNode()
    {
        var parser = new Parser("MODEL ADD ProductModel {id:Integer:key, name:String, price:Double, description:String, active:Boolean} (perm)");
        var result = parser.Parse();

        var modelAdd = Assert.IsType<ModelAddNode>(result);
        Assert.Equal(5, modelAdd.Fields.Count);
        Assert.Equal("perm", modelAdd.ModelType);
    }

    [Fact]
    public void Parse_ModelEditWithMultipleFields_ReturnsModelEditNode()
    {
        var parser = new Parser("MODEL EDIT UserModel {name:String, age:Integer} {fullName:String, years:Integer}");
        var result = parser.Parse();

        var modelEdit = Assert.IsType<ModelEditNode>(result);
        Assert.Equal(2, modelEdit.OldFields.Count);
        Assert.Equal(2, modelEdit.NewFields.Count);
    }

    [Fact]
    public void Parse_ModelDelWithFieldList_ReturnsModelDelNode()
    {
        var parser = new Parser("MODEL DEL UserModel {age, email, phone}");
        var result = parser.Parse();

        var modelDel = Assert.IsType<ModelDelNode>(result);
        Assert.NotNull(modelDel.FieldNames);
        Assert.Equal(3, modelDel.FieldNames.Count);
    }

    #endregion

    #region Error handling

    [Fact]
    public void Parse_InvalidSyntax_ThrowsException()
    {
        var parser = new Parser("SELECT * FROM");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parse_EmptyQuery_ThrowsException()
    {
        var parser = new Parser("");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parse_InvalidToken_ThrowsException()
    {
        // Exception thrown in constructor during tokenization
        Assert.ThrowsAny<Exception>(() => new Parser("SELECT * FROM users WHERE @invalid"));
    }

    [Fact]
    public void Parse_MissingClosingBrace_ThrowsException()
    {
        var parser = new Parser("SELECT * FROM users WHERE age IN (18, 25");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parse_MissingComma_ThrowsException()
    {
        var parser = new Parser("SELECT name age FROM users");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parse_InvalidOperator_ThrowsException()
    {
        var parser = new Parser("SELECT * FROM users WHERE age <> 18");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    #endregion

    #region Edge cases

    [Fact]
    public void Parse_SelectWithVeryLongColumnName_ReturnsSelectNode()
    {
        var longName = new string('a', 100);
        var parser = new Parser($"SELECT {longName} FROM users");
        var result = parser.Parse();

        Assert.IsType<SelectNode>(result);
    }

    [Fact]
    public void Parse_SelectWithUnicodeCharacters_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT name, age FROM users WHERE name LIKE 'John%'");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parse_SelectWithSpecialCharactersInString_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM users WHERE name = 'O''Brien'");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parse_SelectWithNegativeNumbers_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM products WHERE price > -100");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parse_SelectWithDecimalNumbers_ReturnsSelectNode()
    {
        var parser = new Parser("SELECT * FROM products WHERE price = 19.99");
        var result = parser.Parse();

        var select = Assert.IsType<SelectNode>(result);
        Assert.NotNull(select.Where);
    }

    #endregion
}

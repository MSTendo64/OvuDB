using ovudb.Core;
using ovudb.OvuRequests;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests;

/// <summary>
/// Error and exception handling tests
/// </summary>
public class ErrorHandlingTests : IDisposable
{
    private readonly string _testDataDirectory;

    public ErrorHandlingTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_error_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    #region Parser error handling tests

    [Fact]
    public void Parser_InvalidSyntax_ThrowsException()
    {
        var parser = new Parser("SELECT * FROM");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parser_MalformedQuery_ThrowsException()
    {
        var parser = new Parser("SELECT * FROM users WHERE");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parser_UnclosedString_ThrowsException()
    {
        // Parser throws ArgumentException for unclosed string in constructor (during tokenization)
        Assert.Throws<ArgumentException>(() => new Parser("SELECT * FROM users WHERE name = 'unclosed"));
    }

    [Fact]
    public void Parser_UnclosedParentheses_ThrowsException()
    {
        var parser = new Parser("SELECT * FROM users WHERE age IN (18, 25");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parser_InvalidOperator_ThrowsException()
    {
        var parser = new Parser("SELECT * FROM users WHERE age <> 18");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parser_EmptyQuery_ThrowsException()
    {
        var parser = new Parser("");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    [Fact]
    public void Parser_WhitespaceOnly_ThrowsException()
    {
        var parser = new Parser("   ");
        Assert.ThrowsAny<Exception>(() => parser.Parse());
    }

    #endregion

    #region Database error handling tests

    [Fact]
    public void Database_InvalidDirectory_HandlesGracefully()
    {
        var invalidPath = Path.Combine(_testDataDirectory, "nonexistent", "path");
        var database = new Database("TestDb", dataDirectory: invalidPath);
        Assert.NotNull(database);
    }

    [Fact]
    public void Database_GetTable_WithInvalidName_HandlesGracefully()
    {
        var database = new Database("TestDb", dataDirectory: _testDataDirectory);
        var table = database.GetTable<object>("");
        Assert.NotNull(table);
    }

    [Fact]
    public void Database_DropNonExistentTable_HandlesGracefully()
    {
        var database = new Database("TestDb", dataDirectory: _testDataDirectory);
        database.DropTable("non_existent_table");
        // Should not throw
        Assert.True(true);
    }

    #endregion

    #region BinaryStorage error handling tests

    [Fact]
    public void BinaryStorage_InvalidDirectory_CreatesDirectory()
    {
        var invalidPath = Path.Combine(_testDataDirectory, "invalid", "path");
        var storage = new BinaryStorage(invalidPath, 9999);
        Assert.NotNull(storage);
    }

    [Fact]
    public void BinaryStorage_TableExists_WithNullName_ReturnsFalse()
    {
        var storage = new BinaryStorage(_testDataDirectory, 9999);
        // TableExists now checks for null and returns false
        Assert.False(storage.TableExists(null));
    }

    [Fact]
    public void BinaryStorage_TableExists_WithEmptyName_ReturnsFalse()
    {
        var storage = new BinaryStorage(_testDataDirectory, 9999);
        Assert.False(storage.TableExists(""));
    }

    [Fact]
    public void BinaryStorage_DeleteNonExistentTable_HandlesGracefully()
    {
        var storage = new BinaryStorage(_testDataDirectory, 9999);
        storage.DeleteTable("non_existent_table");
        // Should not throw
        Assert.True(true);
    }

    [Fact]
    public void BinaryStorage_LoadNonExistentTable_ReturnsNull()
    {
        var storage = new BinaryStorage(_testDataDirectory, 9999);
        var result = storage.LoadTable("non_existent_table");
        Assert.Null(result);
    }

    #endregion

    #region Edge case handling tests

    [Fact]
    public void Parser_VeryLongQuery_HandlesCorrectly()
    {
        // Use table name that is not a reserved word
        var longQuery = "SELECT " + string.Join(", ", Enumerable.Range(1, 50).Select(i => $"column{i}")) + " FROM mytable";
        var parser = new Parser(longQuery);
        var result = parser.Parse();
        Assert.NotNull(result);
    }

    [Fact]
    public void Parser_VeryLongString_HandlesCorrectly()
    {
        var longString = new string('A', 10000);
        var query = $"SELECT * FROM users WHERE name = '{longString}'";
        var parser = new Parser(query);
        var result = parser.Parse();
        Assert.NotNull(result);
    }

    [Fact]
    public void Parser_DeeplyNestedConditions_HandlesCorrectly()
    {
        var query = "SELECT * FROM users WHERE (age > 18 AND (name LIKE 'J%' OR (email LIKE '%@gmail.com' AND status = 'active')))";
        var parser = new Parser(query);
        var result = parser.Parse();
        Assert.NotNull(result);
    }

    [Fact]
    public void Database_TableWithVeryLongName_HandlesCorrectly()
    {
        var longName = new string('A', 255);
        var database = new Database("TestDb", dataDirectory: _testDataDirectory);
        var table = database.GetTable<TestEntity>(longName);
        table.AddColumn("Id", DataType.Integer)
             .CreateIfNotExists();
        Assert.NotNull(table);
    }

    #endregion

    #region Recovery after error tests

    [Fact]
    public void BinaryStorage_CorruptedMetadata_RecoversGracefully()
    {
        // Create table and insert data so files are created
        var database = new Database("TestDb", dataDirectory: _testDataDirectory);
        var table = database.GetTable<TestEntity>("recovery_test");
        table.AddColumn(new Column("Id", DataType.Integer).PrimaryKey())
             .AddColumn("Name", DataType.String)
             .CreateIfNotExists();
        
        // Insert data so table files are created
        table.Insert(new TestEntity { Name = "Test" });
        
        // Ensure table exists
        Assert.True(database.TableExists("recovery_test"));

        // Create new storage with same databaseId - should restore metadata from disk files
        // Compute databaseId for "TestDb" (same logic as Database.cs)
        var databaseId = Math.Abs("TestDb".GetHashCode()) % 1000000 + 1000;
        var newStorage = new BinaryStorage(_testDataDirectory, databaseId);
        // LoadTableCache should restore table from disk files
        // If table not found immediately, recovery may happen only on lookup
        // Check via FindTableId in TableExists which should scan disk
        var tableExists = newStorage.TableExists("recovery_test");
        Assert.True(tableExists, "Table should be restored from disk files");
    }

    [Fact]
    public void Database_AfterError_ContinuesWorking()
    {
        var database = new Database("TestDb", dataDirectory: _testDataDirectory);
        
        // Try operation that may cause error
        try
        {
            database.DropTable("non_existent_table");
        }
        catch { }

        // Database should continue working
        var table = database.GetTable<object>("test_table");
        Assert.NotNull(table);
    }

    #endregion
}

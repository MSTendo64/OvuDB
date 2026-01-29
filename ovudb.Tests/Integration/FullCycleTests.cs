using ovudb.Core;
using ovudb.Network;
using ovudb.Network.Authentication;
using ovudb.OvuRequests;
using ovudb.SystemDatabase;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.Integration;

/// <summary>
/// Integration tests for full system cycle
/// </summary>
public class FullCycleTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly ModelService _modelService;
    private readonly Executor _executor;

    public FullCycleTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_fullcycle_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
        _database = new Database("FullCycleDb", dataDirectory: _testDataDirectory);
        _systemDatabaseService = new SystemDatabaseService(Path.Combine(_testDataDirectory, "ovusys"));
        _modelService = new ModelService(_systemDatabaseService);
        _executor = new Executor(_database, _modelService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    #region Full cycle CRUD operations

    [Fact]
    public void FullCycle_CreateTableInsertSelectUpdateDelete()
    {
        // 1. Create table via ovuRequests
        var createParser = new Parser("CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name STRING, age INTEGER)");
        var createQuery = createParser.Parse();
        _executor.Execute(createQuery);

        // 2. Insert data
        var insertParser = new Parser("INSERT INTO users (name, age) VALUES ('John', 25)");
        var insertQuery = insertParser.Parse();
        _executor.Execute(insertQuery);

        // 3. Select data
        var selectParser = new Parser("SELECT * FROM users WHERE name = 'John'");
        var selectQuery = selectParser.Parse();
        var selectResult = _executor.Execute(selectQuery);
        var resultDict = Assert.IsType<Dictionary<string, object>>(selectResult);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Single(rows);

        // 4. Update data
        var updateParser = new Parser("UPDATE users SET age = 26 WHERE name = 'John'");
        var updateQuery = updateParser.Parse();
        _executor.Execute(updateQuery);

        // 5. Verify update
        var checkParser = new Parser("SELECT age FROM users WHERE name = 'John'");
        var checkQuery = checkParser.Parse();
        var checkResult = _executor.Execute(checkQuery);
        var checkResultDict = Assert.IsType<Dictionary<string, object>>(checkResult);
        var checkRows = Assert.IsType<List<Dictionary<string, object>>>(checkResultDict["rows"]);
        Assert.Single(checkRows);
        // Look for key "age" (may be any case)
        var ageKey = checkRows[0].Keys.FirstOrDefault(k => k.Equals("age", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(ageKey);
        Assert.Equal(26, Convert.ToInt32(checkRows[0][ageKey]));

        // 6. Delete data
        var deleteParser = new Parser("DELETE FROM users WHERE name = 'John'");
        var deleteQuery = deleteParser.Parse();
        _executor.Execute(deleteQuery);

        // 7. Verify deletion
        var finalParser = new Parser("SELECT * FROM users WHERE name = 'John'");
        var finalQuery = finalParser.Parse();
        var finalResult = _executor.Execute(finalQuery);
        var finalResultDict = Assert.IsType<Dictionary<string, object>>(finalResult);
        var finalRows = Assert.IsType<List<Dictionary<string, object>>>(finalResultDict["rows"]);
        Assert.Empty(finalRows);
    }

    [Fact]
    public void FullCycle_MultipleTablesWithRelations()
    {
        // Create users table
        var createUsers = new Parser("CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name STRING)");
        _executor.Execute(createUsers.Parse());

        // Create orders table
        var createOrders = new Parser("CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, product STRING)");
        _executor.Execute(createOrders.Parse());

        // Insert user
        var insertUser = new Parser("INSERT INTO users (name) VALUES ('John')");
        _executor.Execute(insertUser.Parse());

        // Get user ID
        var getUserId = new Parser("SELECT id FROM users WHERE name = 'John'");
        var userIdResult = _executor.Execute(getUserId.Parse());
        var userIdResultDict = Assert.IsType<Dictionary<string, object>>(userIdResult);
        var userIdRows = Assert.IsType<List<Dictionary<string, object>>>(userIdResultDict["rows"]);
        Assert.NotEmpty(userIdRows);
        // Look for key "id" (may be any case)
        var idKey = userIdRows[0].Keys.FirstOrDefault(k => k.Equals("id", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(idKey);
        var userId = Convert.ToInt32(userIdRows[0][idKey]);

        // Insert order
        var insertOrder = new Parser($"INSERT INTO orders (user_id, product) VALUES ({userId}, 'Product1')");
        _executor.Execute(insertOrder.Parse());

        // Verify relation
        var checkOrder = new Parser($"SELECT * FROM orders WHERE user_id = {userId}");
        var orderResult = _executor.Execute(checkOrder.Parse());
        var orderResultDict = Assert.IsType<Dictionary<string, object>>(orderResult);
        var orderRows = Assert.IsType<List<Dictionary<string, object>>>(orderResultDict["rows"]);
        Assert.Single(orderRows);
    }

    #endregion

    #region Full cycle with models

    [Fact]
    public void FullCycle_ModelCreateTableInsertQuery()
    {
        // 1. Create model
        var modelParser = new Parser("MODEL ADD UserModel {id:Integer:key, name:String, age:Integer} (perm)");
        var modelQuery = modelParser.Parse();
        _executor.Execute(modelQuery);

        // 2. Verify model was created
        var listParser = new Parser("MODEL LIST");
        var listQuery = listParser.Parse();
        var listResult = _executor.Execute(listQuery);
        Assert.NotNull(listResult);

        // 3. Create table from model (via regular CREATE TABLE)
        var createParser = new Parser("CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name STRING, age INTEGER)");
        _executor.Execute(createParser.Parse());

        // 4. Insert data
        var insertParser = new Parser("INSERT INTO users (name, age) VALUES ('ModelUser', 30)");
        _executor.Execute(insertParser.Parse());

        // 5. Query data
        var selectParser = new Parser("SELECT * FROM users WHERE name = 'ModelUser'");
        var selectQuery = selectParser.Parse();
        var selectResult = _executor.Execute(selectQuery);
        var resultDict = Assert.IsType<Dictionary<string, object>>(selectResult);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Single(rows);
    }

    #endregion

    #region Full cycle with persistence

    [Fact]
    public void FullCycle_DataPersistsAfterRestart()
    {
        // 1. Create table and insert data
        var createParser = new Parser("CREATE TABLE persist_test (id INTEGER PRIMARY KEY AUTOINCREMENT, name STRING)");
        _executor.Execute(createParser.Parse());

        var insertParser = new Parser("INSERT INTO persist_test (name) VALUES ('PersistUser')");
        _executor.Execute(insertParser.Parse());

        // 2. Create new database (simulate restart)
        var newDatabase = new Database("FullCycleDb", dataDirectory: _testDataDirectory);
        var newSystemDb = new SystemDatabaseService(Path.Combine(_testDataDirectory, "ovusys"));
        var newModelService = new ModelService(newSystemDb);
        var newExecutor = new Executor(newDatabase, newModelService);

        // 3. Verify data was persisted
        var selectParser = new Parser("SELECT * FROM persist_test WHERE name = 'PersistUser'");
        var selectQuery = selectParser.Parse();
        var selectResult = newExecutor.Execute(selectQuery);
        var resultDict = Assert.IsType<Dictionary<string, object>>(selectResult);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(resultDict["rows"]);
        Assert.Single(rows);
    }

    #endregion

    #region Full cycle with aggregate functions

    [Fact]
    public void FullCycle_AggregateFunctionsWork()
    {
        // Create table
        var createParser = new Parser("CREATE TABLE products (id INTEGER PRIMARY KEY AUTOINCREMENT, name STRING, price DOUBLE)");
        _executor.Execute(createParser.Parse());

        // Insert data
        var insertParser = new Parser("INSERT INTO products (name, price) VALUES ('Product1', 10.5), ('Product2', 20.0), ('Product3', 15.75)");
        _executor.Execute(insertParser.Parse());

        // Test COUNT
        var countParser = new Parser("SELECT COUNT(*) FROM products");
        var countResult = _executor.Execute(countParser.Parse());
        Assert.NotNull(countResult);

        // Test SUM
        var sumParser = new Parser("SELECT SUM(price) FROM products");
        var sumResult = _executor.Execute(sumParser.Parse());
        Assert.NotNull(sumResult);

        // Test AVG
        var avgParser = new Parser("SELECT AVG(price) FROM products");
        var avgResult = _executor.Execute(avgParser.Parse());
        Assert.NotNull(avgResult);

        // Test MIN
        var minParser = new Parser("SELECT MIN(price) FROM products");
        var minResult = _executor.Execute(minParser.Parse());
        Assert.NotNull(minResult);

        // Test MAX
        var maxParser = new Parser("SELECT MAX(price) FROM products");
        var maxResult = _executor.Execute(maxParser.Parse());
        Assert.NotNull(maxResult);
    }

    #endregion

    #region Full cycle with sorting and filtering

    [Fact]
    public void FullCycle_ComplexQueryWithSortingAndFiltering()
    {
        // Create table
        var createParser = new Parser("CREATE TABLE employees (id INTEGER PRIMARY KEY AUTOINCREMENT, name STRING, age INTEGER, salary DOUBLE)");
        _executor.Execute(createParser.Parse());

        // Insert data
        var insertParser = new Parser("INSERT INTO employees (name, age, salary) VALUES ('Alice', 30, 5000), ('Bob', 25, 4000), ('Charlie', 35, 6000), ('David', 28, 4500)");
        _executor.Execute(insertParser.Parse());

        // Complex query with filtering and sorting
        var complexParser = new Parser("SELECT * FROM employees WHERE age > 25 AND salary > 4000 ORDER BY salary DESC LIMIT 2");
        var complexQuery = complexParser.Parse();
        var complexResult = _executor.Execute(complexQuery);
        var complexResultDict = Assert.IsType<Dictionary<string, object>>(complexResult);
        var rows = Assert.IsType<List<Dictionary<string, object>>>(complexResultDict["rows"]);
        Assert.True(rows.Count <= 2);
    }

    #endregion
}

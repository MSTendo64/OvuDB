using ovudb.Core;
using ovudb.OvuRequests;
using ovudb.OvuRequests.Ast;
using ovudb.SystemDatabase;
using System.Diagnostics;
using Xunit;

namespace ovudb.Tests.OvuRequests;

/// <summary>
/// Load tests for performance with large data volumes
/// </summary>
public class LoadTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;
    private readonly Executor _executor;
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly ModelService _modelService;

    public LoadTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_load_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);

        _database = new Database("LoadTestDb", dataDirectory: _testDataDirectory);
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

    [Fact]
    public void LoadTest_InsertLargeDataset_CompletesInReasonableTime()
    {
        // Create table
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 10000;
        var sw = Stopwatch.StartNew();

        // Insert large number of records in batch (optimized)
        // Disable auto-save for max performance
        table.SetAutoSave(false);
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i}",
                Age = 20 + (i % 50),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        sw.Stop();

        // Verify all records inserted
        var allRecords = table.GetAll();
        Assert.Equal(recordCount, allRecords.Count());

        // Verify operation completed in reasonable time (under 30 seconds)
        Assert.True(sw.ElapsedMilliseconds < 30000, 
            $"Inserting {recordCount} records took {sw.ElapsedMilliseconds} ms, too long");
    }

    [Fact]
    public void LoadTest_SelectWithWhereOnLargeDataset_CompletesInReasonableTime()
    {
        // Create table and fill with data
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 50000;
        // Use batch insert for optimization
        table.SetAutoSave(false);
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i}",
                Age = 20 + (i % 50),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        // Test SELECT with WHERE
        var sw = Stopwatch.StartNew();
        var parser = new Parser("SELECT * FROM test_table WHERE age > 30");
        var query = parser.Parse();
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);
        var result = _executor.Execute(optimizedQuery);
        sw.Stop();

        var resultDict = result as Dictionary<string, object>;
        Assert.NotNull(resultDict);
        var rows = resultDict["rows"] as List<Dictionary<string, object?>>;
        Assert.NotNull(rows);
        Assert.True(rows.Count > 0);

        // Verify performance (under 10 seconds for 50k records after optimizations)
        Assert.True(sw.ElapsedMilliseconds < 10000,
            $"SELECT with WHERE on {recordCount} records took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_SelectWithOrderByOnLargeDataset_CompletesInReasonableTime()
    {
        // Create table and fill with data
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 20000;
        var random = new Random(42);
        // Use batch insert for optimization
        table.SetAutoSave(false);
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i}",
                Age = random.Next(18, 80),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        // Test SELECT with ORDER BY
        var sw = Stopwatch.StartNew();
        var parser = new Parser("SELECT * FROM test_table ORDER BY age DESC LIMIT 100");
        var query = parser.Parse();
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);
        var result = _executor.Execute(optimizedQuery);
        sw.Stop();

        var resultDict = result as Dictionary<string, object>;
        Assert.NotNull(resultDict);
        var rows = resultDict["rows"] as List<Dictionary<string, object?>>;
        Assert.NotNull(rows);
        Assert.Equal(100, rows.Count);

        // Verify sorting works correctly
        var ages = rows.Select(r => Convert.ToInt32(r["Age"])).ToList();
        for (int i = 0; i < ages.Count - 1; i++)
        {
            Assert.True(ages[i] >= ages[i + 1], "Descending sort does not work");
        }

        // Verify performance (under 15 seconds for 20k records with ORDER BY)
        Assert.True(sw.ElapsedMilliseconds < 15000,
            $"SELECT with ORDER BY on {recordCount} records took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_SelectWithGroupByOnLargeDataset_CompletesInReasonableTime()
    {
        // Create table and fill with data
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 30000;
        var random = new Random(42);
        // Use batch insert for optimization
        table.SetAutoSave(false);
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i}",
                Age = random.Next(18, 80),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        // Test SELECT with GROUP BY and aggregation
        var sw = Stopwatch.StartNew();
        var parser = new Parser("SELECT age, COUNT(*) as count FROM test_table GROUP BY age");
        var query = parser.Parse();
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);
        var result = _executor.Execute(optimizedQuery);
        sw.Stop();

        var resultDict = result as Dictionary<string, object>;
        Assert.NotNull(resultDict);
        var rows = resultDict["rows"] as List<Dictionary<string, object?>>;
        Assert.NotNull(rows);
        Assert.True(rows.Count > 0);

        // Verify performance (under 10 seconds for 30k records with GROUP BY)
        Assert.True(sw.ElapsedMilliseconds < 10000,
            $"SELECT with GROUP BY on {recordCount} records took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_SelectWithLikeOnLargeDataset_CompletesInReasonableTime()
    {
        // Create table and fill with data
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 25000;
        // Use batch insert for optimization
        table.SetAutoSave(false);
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i:D5}",
                Age = 20 + (i % 50),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        // Test SELECT with LIKE
        var sw = Stopwatch.StartNew();
        var parser = new Parser("SELECT * FROM test_table WHERE name LIKE '%123%'");
        var query = parser.Parse();
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);
        var result = _executor.Execute(optimizedQuery);
        sw.Stop();

        var resultDict = result as Dictionary<string, object>;
        Assert.NotNull(resultDict);
        var rows = resultDict["rows"] as List<Dictionary<string, object?>>;
        Assert.NotNull(rows);
        Assert.True(rows.Count > 0);

        // Verify performance (under 20 seconds for 25k records with LIKE after optimizations)
        Assert.True(sw.ElapsedMilliseconds < 20000,
            $"SELECT with LIKE on {recordCount} records took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_ParseLargeQuery_CompletesQuickly()
    {
        // Generate large query with many conditions
        var conditions = new List<string>();
        for (int i = 0; i < 1000; i++)
        {
            conditions.Add($"age = {20 + (i % 50)}");
        }

        var query = $"SELECT * FROM test_table WHERE {string.Join(" OR ", conditions)}";
        
        var sw = Stopwatch.StartNew();
        var parser = new Parser(query);
        var result = parser.Parse();
        sw.Stop();

        Assert.NotNull(result);
        Assert.IsType<SelectNode>(result);

        // Parsing should be fast (under 1 second)
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"Parsing large query took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_MultipleConcurrentQueries_CompletesSuccessfully()
    {
        // Create table and fill with data
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 10000;
        // Use batch insert for optimization
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i}",
                Age = 20 + (i % 50),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        // Run several queries in parallel
        var tasks = new List<Task>();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 10; i++)
        {
            int queryIndex = i;
            tasks.Add(Task.Run(() =>
            {
                var parser = new Parser($"SELECT * FROM test_table WHERE age = {20 + queryIndex}");
                var query = parser.Parse();
                var optimizer = new Optimizer(_database);
                var optimizedQuery = optimizer.Optimize(query);
                var result = _executor.Execute(optimizedQuery);
                Assert.NotNull(result);
            }));
        }

        Task.WaitAll(tasks.ToArray());
        sw.Stop();

        // All queries should complete successfully
        Assert.True(sw.ElapsedMilliseconds < 10000,
            $"10 parallel queries took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_ModelOperationsWithLargeFields_CompletesSuccessfully()
    {
        // Create model with many fields
        var fields = new List<ovudb.SystemDatabase.Models.ModelField>();
        for (int i = 0; i < 100; i++)
        {
            fields.Add(new ovudb.SystemDatabase.Models.ModelField
            {
                Name = $"Field{i}",
                Type = i % 2 == 0 ? "String" : "Integer",
                IsKey = i == 0
            });
        }

        var sw = Stopwatch.StartNew();
        var result = _modelService.AddModel("LargeModel", fields, "perm");
        sw.Stop();

        Assert.True(result);
        Assert.True(_modelService.ModelExists("LargeModel"));

        // Operation should be fast
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"Creating model with 100 fields took {sw.ElapsedMilliseconds} ms");

        // Verify model view
        var details = _modelService.SeeModel("LargeModel");
        Assert.NotNull(details);
        Assert.Equal(100, details.Fields.Count);
    }

    [Fact]
    public void LoadTest_SelectWithMultipleAggregates_CompletesInReasonableTime()
    {
        // Create table and fill with data
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 40000;
        var random = new Random(42);
        // Use batch insert for optimization
        table.SetAutoSave(false);
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i}",
                Age = random.Next(18, 80),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        // Test SELECT with multiple aggregate functions
        var sw = Stopwatch.StartNew();
        var parser = new Parser("SELECT COUNT(*) as total, SUM(age) as sum_age, AVG(age) as avg_age, MIN(age) as min_age, MAX(age) as max_age FROM test_table");
        var query = parser.Parse();
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);
        var result = _executor.Execute(optimizedQuery);
        sw.Stop();

        var resultDict = result as Dictionary<string, object>;
        Assert.NotNull(resultDict);
        var rows = resultDict["rows"] as List<Dictionary<string, object?>>;
        Assert.NotNull(rows);
        Assert.Single(rows);

        var row = rows[0];
        Assert.NotNull(row["total"]);
        Assert.NotNull(row["sum_age"]);
        Assert.NotNull(row["avg_age"]);
        Assert.NotNull(row["min_age"]);
        Assert.NotNull(row["max_age"]);

        // Verify performance (under 10 seconds for 40k records with aggregates)
        Assert.True(sw.ElapsedMilliseconds < 10000,
            $"SELECT with aggregates on {recordCount} records took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_SelectWithComplexWhere_CompletesInReasonableTime()
    {
        // Create table and fill with data
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 30000;
        var random = new Random(42);
        // Use batch insert for optimization
        table.SetAutoSave(false);
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i}",
                Age = random.Next(18, 80),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        // Test SELECT with complex WHERE condition
        var sw = Stopwatch.StartNew();
        var parser = new Parser("SELECT * FROM test_table WHERE (age > 25 AND age < 50) OR (IsActive = true AND age IN (30, 35, 40))");
        var query = parser.Parse();
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);
        var result = _executor.Execute(optimizedQuery);
        sw.Stop();

        var resultDict = result as Dictionary<string, object>;
        Assert.NotNull(resultDict);
        var rows = resultDict["rows"] as List<Dictionary<string, object?>>;
        Assert.NotNull(rows);
        Assert.True(rows.Count > 0);

        // Verify performance (under 10 seconds for 30k records with complex WHERE)
        Assert.True(sw.ElapsedMilliseconds < 10000,
            $"SELECT with complex WHERE on {recordCount} records took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_SelectWithLimitAndOffset_CompletesInReasonableTime()
    {
        // Create table and fill with data
        var table = _database.GetTable<TestEntity>("test_table");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        const int recordCount = 50000;
        // Use batch insert for optimization
        table.SetAutoSave(false);
        var entities = new List<TestEntity>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            entities.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i}",
                Age = 20 + (i % 50),
                IsActive = i % 2 == 0
            });
        }
        table.InsertBatch(entities);

        // Test SELECT with LIMIT and OFFSET
        var sw = Stopwatch.StartNew();
        var parser = new Parser("SELECT * FROM test_table ORDER BY id LIMIT 100 OFFSET 10000");
        var query = parser.Parse();
        var optimizer = new Optimizer(_database);
        var optimizedQuery = optimizer.Optimize(query);
        var result = _executor.Execute(optimizedQuery);
        sw.Stop();

        var resultDict = result as Dictionary<string, object>;
        Assert.NotNull(resultDict);
        var rows = resultDict["rows"] as List<Dictionary<string, object?>>;
        Assert.NotNull(rows);
        Assert.Equal(100, rows.Count);

        // Verify performance (under 15 seconds for 50k records with LIMIT/OFFSET and ORDER BY)
        Assert.True(sw.ElapsedMilliseconds < 15000,
            $"SELECT with LIMIT and OFFSET on {recordCount} records took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LoadTest_DatabasePersistence_AfterServerRestart_DataSurvives()
    {
        const string dbName = "PersistenceTestDb";
        const int usersCount = 100;
        const int productsCount = 50;
        const int ordersCount = 75;

        // ========== STAGE 1: Create DB and load data ==========
        var database1 = new Database(dbName, dataDirectory: _testDataDirectory);
        
        // Create table Users
        var usersTable = database1.GetTable<TestEntity>("Users");
        usersTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Age", DataType.Integer))
            .AddColumn(new Column("IsActive", DataType.Boolean))
            .CreateIfNotExists();

        // Create table Products
        var productsTable = database1.GetTable<ProductEntity>("Products");
        productsTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("Price", DataType.Double))
            .AddColumn(new Column("Category", DataType.String))
            .CreateIfNotExists();

        // Create table Orders
        var ordersTable = database1.GetTable<OrderEntity>("Orders");
        ordersTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("UserId", DataType.Integer).NotNull())
            .AddColumn(new Column("ProductId", DataType.Integer).NotNull())
            .AddColumn(new Column("Quantity", DataType.Integer))
            .AddColumn(new Column("OrderDate", DataType.DateTime))
            .CreateIfNotExists();

        // Load data into Users table
        usersTable.SetAutoSave(false);
        var users = new List<TestEntity>(usersCount);
        for (int i = 0; i < usersCount; i++)
        {
            users.Add(new TestEntity
            {
                Id = i + 1,
                Name = $"User{i:D3}",
                Age = 20 + (i % 50),
                IsActive = i % 2 == 0
            });
        }
        usersTable.InsertBatch(users);
        usersTable.Flush(); // Force save

        // Load data into Products table
        productsTable.SetAutoSave(false);
        var products = new List<ProductEntity>(productsCount);
        var random = new Random(42);
        for (int i = 0; i < productsCount; i++)
        {
            products.Add(new ProductEntity
            {
                Id = i + 1,
                Name = $"Product{i:D3}",
                Price = Math.Round(10.0 + random.NextDouble() * 990, 2),
                Category = i % 5 == 0 ? "Electronics" : i % 5 == 1 ? "Clothing" : i % 5 == 2 ? "Food" : i % 5 == 3 ? "Books" : "Other"
            });
        }
        productsTable.InsertBatch(products);
        productsTable.Flush(); // Force save

        // Load data into Orders table
        ordersTable.SetAutoSave(false);
        var orders = new List<OrderEntity>(ordersCount);
        for (int i = 0; i < ordersCount; i++)
        {
            orders.Add(new OrderEntity
            {
                Id = i + 1,
                UserId = (i % usersCount) + 1,
                ProductId = (i % productsCount) + 1,
                Quantity = random.Next(1, 10),
                OrderDate = DateTime.Now.AddDays(-random.Next(0, 365))
            });
        }
        ordersTable.InsertBatch(orders);
        ordersTable.Flush(); // Force save

        // Verify data saved before restart
        var usersBefore = usersTable.GetAll().ToList();
        var productsBefore = productsTable.GetAll().ToList();
        var ordersBefore = ordersTable.GetAll().ToList();

        Assert.Equal(usersCount, usersBefore.Count);
        Assert.Equal(productsCount, productsBefore.Count);
        Assert.Equal(ordersCount, ordersBefore.Count);

        // "Restart" server - create new database with same dataDirectory
        database1 = null; // Release reference
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // ========== STAGE 2: Restart - create new DB ==========
        var database2 = new Database(dbName, dataDirectory: _testDataDirectory);

        // Get tables (they should load from disk)
        var usersTable2 = database2.GetTable<TestEntity>("Users");
        usersTable2.CreateIfNotExists(); // Create structure if not exists

        var productsTable2 = database2.GetTable<ProductEntity>("Products");
        productsTable2.CreateIfNotExists();

        var ordersTable2 = database2.GetTable<OrderEntity>("Orders");
        ordersTable2.CreateIfNotExists();

        // ========== STAGE 3: Verify DB content ==========
        var usersAfter = usersTable2.GetAll().ToList();
        var productsAfter = productsTable2.GetAll().ToList();
        var ordersAfter = ordersTable2.GetAll().ToList();

        // Verify record count
        Assert.Equal(usersCount, usersAfter.Count);
        Assert.Equal(productsCount, productsAfter.Count);
        Assert.Equal(ordersCount, ordersAfter.Count);

        // Verify Users content
        Assert.All(usersAfter, user =>
        {
            Assert.True(user.Id > 0);
            Assert.NotEmpty(user.Name);
            Assert.True(user.Age >= 20 && user.Age < 70);
        });

        // Verify specific User records
        var user1 = usersAfter.FirstOrDefault(u => u.Name == "User000");
        Assert.NotNull(user1);
        Assert.Equal(1, user1.Id);
        Assert.Equal(20, user1.Age);

        var user50 = usersAfter.FirstOrDefault(u => u.Name == "User049");
        Assert.NotNull(user50);
        Assert.Equal(50, user50.Id);
        Assert.Equal(69, user50.Age); // 20 + (49 % 50) = 20 + 49 = 69

        // Verify Products content
        Assert.All(productsAfter, product =>
        {
            Assert.True(product.Id > 0);
            Assert.NotEmpty(product.Name);
            Assert.True(product.Price >= 10.0 && product.Price <= 1000.0);
            Assert.NotEmpty(product.Category);
        });

        // Verify specific Product records
        var product1 = productsAfter.FirstOrDefault(p => p.Name == "Product000");
        Assert.NotNull(product1);
        Assert.Equal(1, product1.Id);

        // Verify Orders content
        Assert.All(ordersAfter, order =>
        {
            Assert.True(order.Id > 0);
            Assert.True(order.UserId > 0 && order.UserId <= usersCount);
            Assert.True(order.ProductId > 0 && order.ProductId <= productsCount);
            Assert.True(order.Quantity >= 1 && order.Quantity < 10);
        });

        // Verify Orders reference existing Users and Products
        var allUserIds = usersAfter.Select(u => u.Id).ToHashSet();
        var allProductIds = productsAfter.Select(p => p.Id).ToHashSet();

        Assert.All(ordersAfter, order =>
        {
            Assert.Contains(order.UserId, allUserIds);
            Assert.Contains(order.ProductId, allProductIds);
        });

        // Verify stats via queries
        var activeUsersCount = usersAfter.Count(u => u.IsActive);
        Assert.True(activeUsersCount > 0);
        Assert.True(activeUsersCount <= usersCount);

        var avgPrice = productsAfter.Average(p => p.Price);
        Assert.True(avgPrice > 0);

        var totalOrders = ordersAfter.Count;
        Assert.Equal(ordersCount, totalOrders);
    }
}

/// <summary>
/// Test entity for load tests
/// </summary>
public class TestEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Test entity for products
/// </summary>
public class ProductEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Price { get; set; }
    public string Category { get; set; } = string.Empty;
}

/// <summary>
/// Test entity for orders
/// </summary>
public class OrderEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime OrderDate { get; set; }
}

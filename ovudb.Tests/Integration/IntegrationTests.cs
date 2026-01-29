using ovudb.Core;
using ovudb.Query;
using ovudb.Storage;
using ovudb.Tests.Models;
using Xunit;

namespace ovudb.Tests.Integration;

public class IntegrationTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly Database _database;

    public IntegrationTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        var storage = new FileStorage(Path.Combine(_testDataDirectory, "IntegrationDB"));
        _database = new Database("IntegrationDB", storage);
    }

    [Fact]
    public void FullWorkflow_CreateInsertQueryUpdateDelete()
    {
        // Create table
        var table = _database.CreateTable<TestEntity>("Users");

        // Insert data
        var user1 = table.Insert(new TestEntity { Name = "Ivan", Age = 25, IsActive = true });
        var user2 = table.Insert(new TestEntity { Name = "Maria", Age = 30, IsActive = true });
        var user3 = table.Insert(new TestEntity { Name = "Peter", Age = 22, IsActive = false });

        Assert.True(user1.Id > 0);
        Assert.True(user2.Id > 0);
        Assert.True(user3.Id > 0);

        // Queries
        var allUsers = table.Query().ToList();
        Assert.Equal(3, allUsers.Count);

        var activeUsers = table.Query()
            .Where(e => e.IsActive == true)
            .ToList();
        Assert.Equal(2, activeUsers.Count);

        var olderUsers = table.Query()
            .Where("Age", 23, ComparisonOperator.GreaterThan)
            .OrderBy("Age")
            .ToList();
        Assert.Equal(2, olderUsers.Count);
        Assert.Equal("Ivan", olderUsers[0].Name);

        // Update
        user1.Age = 26;
        table.Update(user1);
        var updated = table.GetById(user1.Id);
        Assert.Equal(26, updated?.Age);

        // Delete
        table.Delete(user3);
        var remaining = table.Query().ToList();
        Assert.Equal(2, remaining.Count);
    }

    [Fact]
    public void Persistence_DataSurvivesRestart()
    {
        // Create and fill
        var table1 = _database.CreateTable<TestEntity>("PersistentTable");
        table1.Insert(new TestEntity { Name = "Persistent", Age = 100 });

        // Create new database with same storage
        var storage2 = new FileStorage(Path.Combine(_testDataDirectory, "IntegrationDB"));
        var database2 = new Database("IntegrationDB", storage2);
        var table2 = database2.GetTable<TestEntity>("PersistentTable");
        table2.CreateIfNotExists();

        // Verify data was persisted
        var all = table2.GetAll();
        Assert.Single(all);
        Assert.Equal("Persistent", all[0].Name);
    }

    [Fact]
    public void ComplexQueries_WorkCorrectly()
    {
        var table = _database.CreateTable<TestEntity>("ComplexTable");

        // Add test data
        for (int i = 1; i <= 10; i++)
        {
            table.Insert(new TestEntity
            {
                Name = $"User{i}",
                Age = 20 + i,
                IsActive = i % 2 == 0
            });
        }

        // Complex query with multiple conditions
        var results = table.Query()
            .Where("Age", 25, ComparisonOperator.GreaterThan)
            .Where("IsActive", true)
            .OrderByDescending("Age")
            .Limit(3)
            .ToList();

        Assert.Equal(3, results.Count);
        Assert.True(results.All(r => r.Age > 25));
        Assert.True(results.All(r => r.IsActive));
    }

    [Fact]
    public void MultipleTables_WorkSimultaneously()
    {
        var usersTable = _database.CreateTable<TestEntity>("Users");
        var productsTable = _database.CreateTable<TestEntityWithoutId>("Products");

        usersTable.Insert(new TestEntity { Name = "User1", Age = 25 });
        productsTable.Insert(new TestEntityWithoutId { Name = "Product1", Price = 100 });

        Assert.Single(usersTable.GetAll());
        Assert.Single(productsTable.GetAll());
    }

    [Fact]
    public void AutoIncrement_WorksCorrectly()
    {
        var table = _database.CreateTable<TestEntityWithLongId>("AutoIncrementTable");

        var entity1 = table.Insert(new TestEntityWithLongId { Description = "First" });
        var entity2 = table.Insert(new TestEntityWithLongId { Description = "Second" });
        var entity3 = table.Insert(new TestEntityWithLongId { Description = "Third" });

        Assert.Equal(1, entity1.EntityId);
        Assert.Equal(2, entity2.EntityId);
        Assert.Equal(3, entity3.EntityId);
    }

    [Fact]
    public void DeleteByCondition_RemovesMultipleRecords()
    {
        var table = _database.CreateTable<TestEntity>("DeleteTable");

        table.Insert(new TestEntity { Name = "Active1", IsActive = true });
        table.Insert(new TestEntity { Name = "Active2", IsActive = true });
        table.Insert(new TestEntity { Name = "Inactive1", IsActive = false });

        var deleted = table.Delete(e => e.IsActive == false);

        Assert.Equal(1, deleted);
        var remaining = table.Query().ToList();
        Assert.Equal(2, remaining.Count);
        Assert.All(remaining, r => Assert.True(r.IsActive));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

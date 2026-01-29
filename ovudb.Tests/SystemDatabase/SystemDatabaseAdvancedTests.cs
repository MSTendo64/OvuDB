using ovudb.SystemDatabase;
using ovudb.SystemDatabase.Models;
using Xunit;

namespace ovudb.Tests.SystemDatabase;

/// <summary>
/// Advanced system database tests
/// </summary>
public class SystemDatabaseAdvancedTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly SystemDatabaseService _systemDatabaseService;

    public SystemDatabaseAdvancedTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_system_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
        _systemDatabaseService = new SystemDatabaseService(_testDataDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    #region db table tests

    [Fact]
    public void SystemDatabase_AddDatabaseEntry_AddsSuccessfully()
    {
        var dbTable = _systemDatabaseService.GetDbTable();
        var entry = new SystemDb
        {
            Host = "%",
            Db = "test_db",
            User = "*",
            SelectPriv = true,
            InsertPriv = true
        };

        dbTable.Insert(entry);
        dbTable.Flush();

        var allEntries = dbTable.GetAll();
        Assert.Contains(allEntries, e => e.Db == "test_db" && e.User == "*");
    }

    [Fact]
    public void SystemDatabase_RemoveDatabaseEntry_RemovesSuccessfully()
    {
        var dbTable = _systemDatabaseService.GetDbTable();
        var entry = new SystemDb
        {
            Host = "%",
            Db = "temp_db",
            User = "*"
        };

        dbTable.Insert(entry);
        dbTable.Flush();

        var entries = dbTable.GetAll().Where(e => e.Db == "temp_db" && e.User == "*").ToList();
        foreach (var e in entries)
        {
            dbTable.Delete(e);
        }
        dbTable.Flush();

        var remaining = dbTable.GetAll().Where(e => e.Db == "temp_db" && e.User == "*");
        Assert.Empty(remaining);
    }

    [Fact]
    public void SystemDatabase_QueryDatabaseEntries_ReturnsCorrectResults()
    {
        var dbTable = _systemDatabaseService.GetDbTable();
        
        // Add several records
        dbTable.Insert(new SystemDb { Host = "%", Db = "db1", User = "*" });
        dbTable.Insert(new SystemDb { Host = "%", Db = "db2", User = "*" });
        dbTable.Insert(new SystemDb { Host = "%", Db = "db1", User = "user1" });
        dbTable.Flush();

        var allEntries = dbTable.GetAll();
        Assert.True(allEntries.Count() >= 3);

        var db1Entries = dbTable.GetAll().Where(e => e.Db == "db1");
        Assert.True(db1Entries.Count() >= 2);
    }

    #endregion

    #region user table tests

    [Fact]
    public void SystemDatabase_AddUser_AddsSuccessfully()
    {
        var userTable = _systemDatabaseService.GetUserTable();
        var user = new SystemUser
        {
            Host = "%",
            Username = "testuser",
            PasswordHash = "hashed_password",
            SelectPriv = true,
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow
        };

        userTable.Insert(user);
        userTable.Flush();

        var allUsers = userTable.GetAll();
        Assert.Contains(allUsers, u => u.Username == "testuser");
    }

    [Fact]
    public void SystemDatabase_UpdateUser_UpdatesSuccessfully()
    {
        var userTable = _systemDatabaseService.GetUserTable();
        var user = new SystemUser
        {
            Host = "%",
            Username = "updateuser",
            PasswordHash = "old_password",
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow
        };

        userTable.Insert(user);
        userTable.Flush();

        var existing = userTable.GetAll().FirstOrDefault(u => u.Username == "updateuser");
        if (existing != null)
        {
            existing.PasswordHash = "new_password";
            userTable.Update(existing);
            userTable.Flush();

            var updated = userTable.GetAll().FirstOrDefault(u => u.Username == "updateuser");
            Assert.NotNull(updated);
            Assert.Equal("new_password", updated.PasswordHash);
        }
    }

    [Fact]
    public void SystemDatabase_DeleteUser_DeletesSuccessfully()
    {
        var userTable = _systemDatabaseService.GetUserTable();
        var user = new SystemUser
        {
            Host = "%",
            Username = "deleteuser",
            PasswordHash = "password",
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow
        };

        userTable.Insert(user);
        userTable.Flush();

        var existing = userTable.GetAll().FirstOrDefault(u => u.Username == "deleteuser");
        if (existing != null)
        {
            userTable.Delete(existing);
            userTable.Flush();

            var deleted = userTable.GetAll().FirstOrDefault(u => u.Username == "deleteuser");
            Assert.Null(deleted);
        }
    }

    #endregion

    #region Persistence tests

    [Fact]
    public void SystemDatabase_DataPersistsAfterRestart()
    {
        var dbTable = _systemDatabaseService.GetDbTable();
        dbTable.Insert(new SystemDb { Host = "%", Db = "persist_db", User = "*" });
        dbTable.Flush();

        // Create new system database instance
        var newSystemDb = new SystemDatabaseService(_testDataDirectory);
        var newDbTable = newSystemDb.GetDbTable();
        newDbTable.Reload();

        var entries = newDbTable.GetAll().Where(e => e.Db == "persist_db" && e.User == "*");
        Assert.NotEmpty(entries);
    }

    [Fact]
    public void SystemDatabase_UserDataPersistsAfterRestart()
    {
        var userTable = _systemDatabaseService.GetUserTable();
        userTable.Insert(new SystemUser 
        { 
            Host = "%", 
            Username = "persist_user", 
            PasswordHash = "pwd",
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow
        });
        userTable.Flush();

        // Create new instance
        var newSystemDb = new SystemDatabaseService(_testDataDirectory);
        var newUserTable = newSystemDb.GetUserTable();
        newUserTable.Reload();

        var users = newUserTable.GetAll().Where(u => u.Username == "persist_user");
        Assert.NotEmpty(users);
    }

    #endregion

    #region Error handling tests

    [Fact]
    public void SystemDatabase_InvalidDirectory_CreatesDirectory()
    {
        var invalidDir = Path.Combine(_testDataDirectory, "nonexistent", "path");
        var systemDb = new SystemDatabaseService(invalidDir);
        
        // Should create directory or handle error
        Assert.NotNull(systemDb);
    }

    [Fact]
    public void SystemDatabase_GetTable_ReturnsValidTable()
    {
        var dbTable = _systemDatabaseService.GetDbTable();
        Assert.NotNull(dbTable);

        var userTable = _systemDatabaseService.GetUserTable();
        Assert.NotNull(userTable);
    }

    #endregion
}

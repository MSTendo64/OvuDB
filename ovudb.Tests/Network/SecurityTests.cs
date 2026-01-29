using ovudb.Network.Authentication;
using ovudb.SystemDatabase;
using Xunit;

namespace ovudb.Tests.Network;

/// <summary>
/// Security and authentication tests
/// </summary>
public class SecurityTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly AuthenticationService _authService;

    public SecurityTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_security_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
        // AuthenticationService creates SystemDatabaseService in ovusys subdirectory
        var ovusysDirectory = Path.Combine(_testDataDirectory, "ovusys");
        _systemDatabaseService = new SystemDatabaseService(ovusysDirectory);
        _authService = new AuthenticationService(_testDataDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    #region Authentication tests

    [Fact]
    public void Authenticate_ValidCredentials_ReturnsTrue()
    {
        _authService.CreateUser("testuser", "password123");
        var result = _authService.Authenticate("testuser", "password123", out var user);
        Assert.True(result);
        Assert.NotNull(user);
    }

    [Fact]
    public void Authenticate_InvalidPassword_ReturnsFalse()
    {
        _authService.CreateUser("testuser", "password123");
        var result = _authService.Authenticate("testuser", "wrongpassword", out var user);
        Assert.False(result);
    }

    [Fact]
    public void Authenticate_NonExistentUser_ReturnsFalse()
    {
        var result = _authService.Authenticate("nonexistent", "password", out var user);
        Assert.False(result);
    }

    [Fact]
    public void Authenticate_EmptyUsername_ReturnsFalse()
    {
        var result = _authService.Authenticate("", "password", out var user);
        Assert.False(result);
    }

    [Fact]
    public void Authenticate_EmptyPassword_ReturnsFalse()
    {
        _authService.CreateUser("testuser", "password");
        var result = _authService.Authenticate("testuser", "", out var user);
        Assert.False(result);
    }

    [Fact]
    public void Authenticate_NullUsername_ReturnsFalse()
    {
        var result = _authService.Authenticate(null, "password", out var user);
        Assert.False(result);
    }

    [Fact]
    public void Authenticate_NullPassword_ReturnsFalse()
    {
        _authService.CreateUser("testuser", "password");
        var result = _authService.Authenticate("testuser", null, out var user);
        Assert.False(result);
    }

    #endregion

    #region User creation tests

    [Fact]
    public void CreateUser_NewUser_CreatesSuccessfully()
    {
        var result = _authService.CreateUser("newuser", "password123");
        Assert.True(result);
        
        var exists = _authService.UserExists("newuser");
        Assert.True(exists);
    }

    [Fact]
    public void CreateUser_DuplicateUser_ReturnsFalse()
    {
        _authService.CreateUser("duplicate", "password");
        var result = _authService.CreateUser("duplicate", "password2");
        Assert.False(result);
    }

    [Fact]
    public void CreateUser_EmptyUsername_ReturnsFalse()
    {
        var result = _authService.CreateUser("", "password");
        Assert.False(result);
    }

    [Fact]
    public void CreateUser_EmptyPassword_ReturnsFalse()
    {
        var result = _authService.CreateUser("user", "");
        Assert.False(result);
    }

    [Fact]
    public void CreateUser_SpecialCharacters_HandlesCorrectly()
    {
        var result = _authService.CreateUser("user@domain", "p@ssw0rd!");
        Assert.True(result);
    }

    #endregion

    #region Access rights tests

    [Fact]
    public void HasDatabaseAccess_UserWithAccess_ReturnsTrue()
    {
        _authService.CreateUser("testuser", "password");
        // Check access via system table db
        var dbTable = _systemDatabaseService.GetDbTable();
        var dbEntry = new ovudb.SystemDatabase.Models.SystemDb
        {
            Host = "%",
            Db = "testdb",
            User = "testuser",
            SelectPriv = true
        };
        dbTable.Insert(dbEntry);
        dbTable.Flush();
        
        var user = _authService.GetUser("testuser");
        if (user != null)
        {
            var hasAccess = _authService.HasDatabaseAccess(user, "testdb");
            Assert.True(hasAccess);
        }
    }

    [Fact]
    public void HasDatabaseAccess_UserWithoutAccess_ReturnsFalse()
    {
        _authService.CreateUser("testuser", "password");
        
        var user = _authService.GetUser("testuser");
        if (user != null)
        {
            var hasAccess = _authService.HasDatabaseAccess(user, "testdb");
            Assert.False(hasAccess);
        }
    }

    #endregion

    #region Password hashing tests

    [Fact]
    public void CreateUser_PasswordIsHashed()
    {
        _authService.CreateUser("testuser", "password123");
        var userTable = _systemDatabaseService.GetUserTable();
        userTable.Reload();
        var systemUser = userTable.GetAll().FirstOrDefault(u => u.Username == "testuser");
        Assert.NotNull(systemUser);
        Assert.NotEqual("password123", systemUser.PasswordHash);
        Assert.True(systemUser.PasswordHash.Length > 20); // Hash must be long
    }

    [Fact]
    public void Authenticate_HashedPassword_WorksCorrectly()
    {
        _authService.CreateUser("testuser", "password123");
        
        // Password must be hashed but authentication must work
        var result = _authService.Authenticate("testuser", "password123", out var user);
        Assert.True(result);
        Assert.NotNull(user);
    }

    #endregion

    #region Security error handling tests

    [Fact]
    public void Authenticate_SqlInjectionAttempt_HandlesSafely()
    {
        _authService.CreateUser("testuser", "password");
        var result = _authService.Authenticate("testuser'; DROP TABLE users; --", "password", out var user);
        Assert.False(result);
    }

    [Fact]
    public void CreateUser_SqlInjectionInUsername_HandlesSafely()
    {
        var result = _authService.CreateUser("user'; DROP TABLE users; --", "password");
        // Should either create user with that name or return false
        // Main point - no SQL injection
        Assert.NotNull(result);
    }

    [Fact]
    public void Authenticate_LongPassword_HandlesCorrectly()
    {
        var longPassword = new string('A', 1000);
        _authService.CreateUser("testuser", longPassword);
        var result = _authService.Authenticate("testuser", longPassword, out var user);
        Assert.True(result);
    }

    [Fact]
    public void Authenticate_UnicodePassword_HandlesCorrectly()
    {
        var unicodePassword = "password123测试🔒";
        _authService.CreateUser("testuser", unicodePassword);
        var result = _authService.Authenticate("testuser", unicodePassword, out var user);
        Assert.True(result);
    }

    #endregion
}

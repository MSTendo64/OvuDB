using ovudb.Network.Authentication;
using Xunit;

namespace ovudb.Tests.Network;

public class AuthenticationServiceTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly AuthenticationService _authService;

    public AuthenticationServiceTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_auth_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
        _authService = new AuthenticationService(_testDataDirectory);
    }

    [Fact]
    public void Authenticate_DefaultUser_Success()
    {
        var authenticated = _authService.Authenticate("admin", "admin", out var user);

        Assert.True(authenticated);
        Assert.NotNull(user);
        Assert.Equal("admin", user!.Username);
        Assert.NotNull(user.PasswordHash);
        Assert.NotEmpty(user.Privileges);
        Assert.NotEmpty(user.Databases);
    }

    [Fact]
    public void Authenticate_DefaultUser_UpdatesLastLogin()
    {
        var authenticated1 = _authService.Authenticate("admin", "admin", out var user1);
        Assert.True(authenticated1);
        var firstLogin = user1!.LastLogin;

        Thread.Sleep(10);
        var authenticated2 = _authService.Authenticate("admin", "admin", out var user2);
        Assert.True(authenticated2);
        var secondLogin = user2!.LastLogin;

        Assert.True(secondLogin > firstLogin);
    }

    [Fact]
    public void Authenticate_WrongPassword_Fails()
    {
        var authenticated = _authService.Authenticate("admin", "wrongpassword", out var user);

        Assert.False(authenticated);
        Assert.Null(user);
    }

    [Fact]
    public void Authenticate_EmptyPassword_Fails()
    {
        var authenticated = _authService.Authenticate("admin", "", out var user);

        Assert.False(authenticated);
        Assert.Null(user);
    }

    [Fact]
    public void Authenticate_EmptyUsername_Fails()
    {
        var authenticated = _authService.Authenticate("", "admin", out var user);

        Assert.False(authenticated);
        Assert.Null(user);
    }

    [Fact]
    public void Authenticate_NonExistentUser_Fails()
    {
        var authenticated = _authService.Authenticate("nonexistent", "password", out var user);

        Assert.False(authenticated);
        Assert.Null(user);
    }

    [Fact]
    public void CreateUser_NewUser_Success()
    {
        var created = _authService.CreateUser("testuser", "testpass");

        Assert.True(created);

        var authenticated = _authService.Authenticate("testuser", "testpass", out var user);
        Assert.True(authenticated);
        Assert.NotNull(user);
        Assert.Equal("testuser", user!.Username);
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual("testpass", user.PasswordHash); // Password must be hashed
    }

    [Fact]
    public void CreateUser_WithCustomPrivileges_Success()
    {
        var privileges = new List<string> { "SELECT", "INSERT" };
        var databases = new List<string> { "db1", "db2" };
        
        var created = _authService.CreateUser("customuser", "pass", databases, privileges);
        Assert.True(created);

        var authenticated = _authService.Authenticate("customuser", "pass", out var user);
        Assert.True(authenticated);
        Assert.NotNull(user);
        Assert.Equal(2, user!.Privileges.Count);
        Assert.Contains("SELECT", user.Privileges);
        Assert.Contains("INSERT", user.Privileges);
        Assert.Equal(2, user.Databases.Count);
        Assert.Contains("db1", user.Databases);
        Assert.Contains("db2", user.Databases);
    }

    [Fact]
    public void CreateUser_WithDefaultPrivileges_Success()
    {
        var created = _authService.CreateUser("defaultuser", "pass");
        Assert.True(created);

        var authenticated = _authService.Authenticate("defaultuser", "pass", out var user);
        Assert.True(authenticated);
        Assert.NotNull(user);
        // User without specified databases must not have automatic "*" access
        // This enforces least privilege
        Assert.DoesNotContain("*", user!.Databases);
        Assert.Empty(user.Databases); // Database list must be empty
        Assert.Contains("SELECT", user.Privileges);
        Assert.Contains("INSERT", user.Privileges);
    }

    [Fact]
    public void CreateUser_DuplicateUser_Fails()
    {
        _authService.CreateUser("duplicate", "pass1");
        var created = _authService.CreateUser("duplicate", "pass2");

        Assert.False(created);
    }

    [Fact]
    public void HasPrivilege_UserWithPrivilege_ReturnsTrue()
    {
        _authService.Authenticate("admin", "admin", out var user);
        Assert.NotNull(user);

        Assert.True(_authService.HasPrivilege(user!, "SELECT"));
        Assert.True(_authService.HasPrivilege(user!, "INSERT"));
        Assert.True(_authService.HasPrivilege(user!, "UPDATE"));
        Assert.True(_authService.HasPrivilege(user!, "DELETE"));
    }

    [Fact]
    public void HasPrivilege_UserWithWildcard_ReturnsTrue()
    {
        _authService.CreateUser("wildcarduser", "pass", null, new List<string> { "*" });
        _authService.Authenticate("wildcarduser", "pass", out var user);
        Assert.NotNull(user);

        Assert.True(_authService.HasPrivilege(user!, "SELECT"));
        Assert.True(_authService.HasPrivilege(user!, "ANY_PRIVILEGE"));
    }

    [Fact]
    public void HasPrivilege_UserWithoutPrivilege_ReturnsFalse()
    {
        _authService.CreateUser("limiteduser", "pass", null, new List<string> { "SELECT" });
        _authService.Authenticate("limiteduser", "pass", out var user);
        Assert.NotNull(user);

        Assert.True(_authService.HasPrivilege(user!, "SELECT"));
        Assert.False(_authService.HasPrivilege(user!, "DELETE"));
        Assert.False(_authService.HasPrivilege(user!, "DROP"));
    }

    [Fact]
    public void HasDatabaseAccess_UserWithWildcard_ReturnsTrue()
    {
        _authService.Authenticate("admin", "admin", out var user);
        Assert.NotNull(user);

        Assert.True(_authService.HasDatabaseAccess(user!, "mydb"));
        Assert.True(_authService.HasDatabaseAccess(user!, "anydb"));
        Assert.True(_authService.HasDatabaseAccess(user!, "anotherdb"));
    }

    [Fact]
    public void HasDatabaseAccess_UserWithSpecificDatabase_ReturnsTrue()
    {
        _authService.CreateUser("dbuser", "pass", new List<string> { "db1", "db2" });
        _authService.Authenticate("dbuser", "pass", out var user);
        Assert.NotNull(user);

        Assert.True(_authService.HasDatabaseAccess(user!, "db1"));
        Assert.True(_authService.HasDatabaseAccess(user!, "db2"));
        Assert.False(_authService.HasDatabaseAccess(user!, "db3"));
    }

    [Fact]
    public void PasswordHash_DifferentPasswords_DifferentHashes()
    {
        // Use unique usernames for each test run
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var username1 = $"user1_{uniqueId}";
        var username2 = $"user2_{uniqueId}";
        
        var created1 = _authService.CreateUser(username1, "password1");
        var created2 = _authService.CreateUser(username2, "password2");

        Assert.True(created1, $"Failed to create user {username1}");
        Assert.True(created2, $"Failed to create user {username2}");

        // Allow time for persistence
        Thread.Sleep(100);

        var authenticated1 = _authService.Authenticate(username1, "password1", out var user1);
        var authenticated2 = _authService.Authenticate(username2, "password2", out var user2);

        Assert.True(authenticated1, $"Failed to authenticate user {username1}");
        Assert.True(authenticated2, $"Failed to authenticate user {username2}");
        Assert.NotNull(user1);
        Assert.NotNull(user2);
        // Hashes of different passwords must differ
        Assert.NotEqual(user1!.PasswordHash, user2!.PasswordHash);
    }

    [Fact]
    public void PasswordHash_SamePassword_SameHash()
    {
        _authService.CreateUser("user1", "samepass");
        _authService.CreateUser("user2", "samepass");

        _authService.Authenticate("user1", "samepass", out var user1);
        _authService.Authenticate("user2", "samepass", out var user2);

        Assert.NotNull(user1);
        Assert.NotNull(user2);
        // Hashes should be same for same passwords
        Assert.Equal(user1!.PasswordHash, user2!.PasswordHash);
    }

    [Fact]
    public void CreateUser_PersistsToFile()
    {
        _authService.CreateUser("persistentuser", "pass");
        
        // Create new service instance which should load user from file
        var newAuthService = new AuthenticationService(_testDataDirectory);
        var authenticated = newAuthService.Authenticate("persistentuser", "pass", out var user);

        Assert.True(authenticated);
        Assert.NotNull(user);
        Assert.Equal("persistentuser", user!.Username);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }
}

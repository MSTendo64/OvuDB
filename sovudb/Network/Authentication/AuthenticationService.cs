using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ovudb.Core;
using ovudb.SystemDatabase;
using ovudb.SystemDatabase.Models;

namespace ovudb.Network.Authentication;

/// <summary>
/// User authentication service.
/// </summary>
public class AuthenticationService
{
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly Table<SystemUser> _usersTable;

    public AuthenticationService(string dataDirectory = "data")
    {
        // Resolve ovusys directory path (inside data directory)
        var dataDirPath = Path.IsPathRooted(dataDirectory) 
            ? dataDirectory 
            : Path.Combine(Directory.GetCurrentDirectory(), dataDirectory);
        var ovusysDirectory = Path.Combine(dataDirPath, "ovusys");
        
        // Initialize system database in ovusys directory
        _systemDatabaseService = new SystemDatabaseService(ovusysDirectory);
        
        // Use system user table
        _usersTable = _systemDatabaseService.GetUserTable();
        
        // Create table if not exists
        _usersTable.CreateIfNotExists();
        
        // On first run (no users), create default admin user for tests and backward compatibility
        try
        {
            // Check for existing users without calling IsSystemDatabaseValid to avoid recursion
            _usersTable.Reload();
            var allUsers = _usersTable.GetAll();
            if (allUsers.Count == 0)
            {
                CreateDefaultAdminUser();
            }
        }
        catch
        {
            // Ignore errors when creating default user
        }
    }

    /// <summary>
    /// Check if system database exists and is valid.
    /// </summary>
    public bool IsSystemDatabaseValid()
    {
        try
        {
            _usersTable.CreateIfNotExists();
            _usersTable.Reload();
            var allUsers = _usersTable.GetAll();
            return allUsers.Count > 0;
        }
        catch (Exception)
        {
            // Assume database is corrupted or missing
            return false;
        }
    }

    /// <summary>
    /// Create default admin user.
    /// </summary>
    public void CreateDefaultAdminUser()
    {
        try
        {
            var allUsers = _usersTable.GetAll();
            var existingUser = allUsers.FirstOrDefault(u => u.Username == "admin");
            
            // Skip if admin already exists
            if (existingUser != null)
            {
                return;
            }
            
            // Create admin user
            var adminUser = new SystemUser
            {
                Username = "admin",
                PasswordHash = HashPassword("admin"),
                Host = "%",
                SelectPriv = true,
                InsertPriv = true,
                UpdatePriv = true,
                DeletePriv = true,
                CreatePriv = true,
                DropPriv = true,
                ReloadPriv = true,
                ShutdownPriv = true,
                ProcessPriv = true,
                FilePriv = true,
                GrantPriv = true,
                ReferencesPriv = true,
                IndexPriv = true,
                AlterPriv = true,
                CreatedAt = DateTime.UtcNow,
                LastLogin = DateTime.MinValue
            };
            _usersTable.Insert(adminUser);
            _usersTable.Flush();
            _usersTable.Reload();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create default admin user: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Hash password.
    /// </summary>
    private string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty", nameof(password));
        }
        
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Authenticate user.
    /// </summary>
    public bool Authenticate(string username, string password, out User? user)
    {
        user = null;
        
        // Validate input
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return false;
        }
        
        _usersTable.Reload();
        var allUsers = _usersTable.GetAll();
        var systemUser = allUsers.FirstOrDefault(u => u.Username == username && (u.Host == "%" || u.Host == "localhost"));
        
        if (systemUser != null)
        {
            try
            {
                var passwordHash = HashPassword(password);
                if (systemUser.PasswordHash != passwordHash)
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                // Password empty or null
                return false;
            }

            systemUser.LastLogin = DateTime.UtcNow;
            _usersTable.Update(systemUser);
            user = ConvertSystemUserToUser(systemUser);
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Get user by username (without password check).
    /// </summary>
    public User? GetUser(string username)
    {
        _usersTable.Reload();
        var allUsers = _usersTable.GetAll();
        var systemUser = allUsers.FirstOrDefault(u => u.Username == username && (u.Host == "%" || u.Host == "localhost"));
        
        if (systemUser != null)
        {
            return ConvertSystemUserToUser(systemUser);
        }
        
        return null;
    }

    /// <summary>
    /// Convert SystemUser to User.
    /// </summary>
    private User ConvertSystemUserToUser(SystemUser systemUser)
    {
        var privileges = new List<string>();
        if (systemUser.SelectPriv) privileges.Add("SELECT");
        if (systemUser.InsertPriv) privileges.Add("INSERT");
        if (systemUser.UpdatePriv) privileges.Add("UPDATE");
        if (systemUser.DeletePriv) privileges.Add("DELETE");
        if (systemUser.CreatePriv) privileges.Add("CREATE");
        if (systemUser.DropPriv) privileges.Add("DROP");
        if (systemUser.ReloadPriv) privileges.Add("RELOAD");
        if (systemUser.ShutdownPriv) privileges.Add("SHUTDOWN");
        if (systemUser.ProcessPriv) privileges.Add("PROCESS");
        if (systemUser.FilePriv) privileges.Add("FILE");
        if (systemUser.GrantPriv) privileges.Add("GRANT");
        if (systemUser.ReferencesPriv) privileges.Add("REFERENCES");
        if (systemUser.IndexPriv) privileges.Add("INDEX");
        if (systemUser.AlterPriv) privileges.Add("ALTER");
        
        // If all privileges are set, add "*" for compatibility
        if (systemUser.SelectPriv && systemUser.InsertPriv && systemUser.UpdatePriv && 
            systemUser.DeletePriv && systemUser.CreatePriv && systemUser.DropPriv &&
            systemUser.ReloadPriv && systemUser.ShutdownPriv && systemUser.ProcessPriv &&
            systemUser.FilePriv && systemUser.GrantPriv && systemUser.ReferencesPriv &&
            systemUser.IndexPriv && systemUser.AlterPriv)
        {
            privileges.Add("*");
        }

        var databases = GetUserDatabases(systemUser.Username);
        // If no db entries and user has all privileges (admin-like), add "*" for access to all databases (least privilege for non-admin users)
        if (databases.Count == 0)
        {
            var hasAllPrivileges = systemUser.SelectPriv && systemUser.InsertPriv && systemUser.UpdatePriv && 
                systemUser.DeletePriv && systemUser.CreatePriv && systemUser.DropPriv &&
                systemUser.ReloadPriv && systemUser.ShutdownPriv && systemUser.ProcessPriv &&
                systemUser.FilePriv && systemUser.GrantPriv && systemUser.ReferencesPriv &&
                systemUser.IndexPriv && systemUser.AlterPriv;
            
            if (hasAllPrivileges)
            {
                databases.Add("*");
            }
        }

        return new User
        {
            Username = systemUser.Username,
            PasswordHash = systemUser.PasswordHash,
            Databases = databases,
            Privileges = privileges,
            CreatedAt = systemUser.CreatedAt,
            LastLogin = systemUser.LastLogin
        };
    }

    /// <summary>
    /// Get list of databases for user from db table.
    /// </summary>
    private List<string> GetUserDatabases(string username)
    {
        try
        {
            var dbTable = _systemDatabaseService.GetDbTable();
            dbTable.Reload();
            var dbEntries = dbTable.GetAll()
                .Where(db => db.User == username && (db.Host == "%" || db.Host == "localhost"))
                .Select(db => db.Db)
                .Distinct()
                .ToList();
            
            return dbEntries;
        }
        catch
        {
            // On error return empty list
            return new List<string>();
        }
    }

    /// <summary>
    /// Parse comma-separated list string.
    /// </summary>
    private List<string> ParseList(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new List<string>();
        }
        
        if (value == "*")
        {
            return new List<string> { "*" };
        }
        
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }

    /// <summary>
    /// Check user privilege.
    /// </summary>
    public bool HasPrivilege(User user, string privilege)
    {
        return user.Privileges.Contains(privilege) || user.Privileges.Contains("*");
    }

    /// <summary>
    /// Check database access.
    /// </summary>
    public bool HasDatabaseAccess(User user, string databaseName)
    {
        return user.Databases.Contains(databaseName) || user.Databases.Contains("*");
    }

    /// <summary>
    /// Create new user.
    /// </summary>
    public bool CreateUser(string username, string password, List<string>? databases = null, List<string>? privileges = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return false;
        
        // Check if user already exists
        var allUsers = _usersTable.GetAll();
        var existingUser = allUsers.FirstOrDefault(u => u.Username == username);
        
        if (existingUser != null)
        {
            return false;
        }

        // Resolve privileges; "*" means all privileges
        var hasAllPrivileges = privileges?.Contains("*") ?? false;
        var hasSelect = hasAllPrivileges || (privileges?.Contains("SELECT") ?? true);
        var hasInsert = hasAllPrivileges || (privileges?.Contains("INSERT") ?? true);
        var hasUpdate = hasAllPrivileges || (privileges?.Contains("UPDATE") ?? true);
        var hasDelete = hasAllPrivileges || (privileges?.Contains("DELETE") ?? true);
        var hasCreate = hasAllPrivileges || (privileges?.Contains("CREATE") ?? false);
        var hasDrop = hasAllPrivileges || (privileges?.Contains("DROP") ?? false);
        var hasReload = hasAllPrivileges || (privileges?.Contains("RELOAD") ?? false);
        var hasShutdown = hasAllPrivileges || (privileges?.Contains("SHUTDOWN") ?? false);
        var hasProcess = hasAllPrivileges || (privileges?.Contains("PROCESS") ?? false);
        var hasFile = hasAllPrivileges || (privileges?.Contains("FILE") ?? false);
        var hasGrant = hasAllPrivileges || (privileges?.Contains("GRANT") ?? false);
        var hasReferences = hasAllPrivileges || (privileges?.Contains("REFERENCES") ?? false);
        var hasIndex = hasAllPrivileges || (privileges?.Contains("INDEX") ?? false);
        var hasAlter = hasAllPrivileges || (privileges?.Contains("ALTER") ?? false);

        var systemUser = new SystemUser
        {
            Username = username,
            PasswordHash = HashPassword(password),
            Host = "%",
            SelectPriv = hasSelect,
            InsertPriv = hasInsert,
            UpdatePriv = hasUpdate,
            DeletePriv = hasDelete,
            CreatePriv = hasCreate,
            DropPriv = hasDrop,
            ReloadPriv = hasReload,
            ShutdownPriv = hasShutdown,
            ProcessPriv = hasProcess,
            FilePriv = hasFile,
            GrantPriv = hasGrant,
            ReferencesPriv = hasReferences,
            IndexPriv = hasIndex,
            AlterPriv = hasAlter,
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.MinValue
        };

        _usersTable.Insert(systemUser);
        _usersTable.Flush();
        
        // If databases specified, add entries to db table
        if (databases != null && databases.Count > 0)
        {
            var dbTable = _systemDatabaseService.GetDbTable();
            foreach (var dbName in databases)
            {
                var dbEntry = new SystemDb
                {
                    Host = "%",
                    Db = dbName,
                    User = username,
                    SelectPriv = hasSelect,
                    InsertPriv = hasInsert,
                    UpdatePriv = hasUpdate,
                    DeletePriv = hasDelete,
                    CreatePriv = hasCreate,
                    DropPriv = hasDrop
                };
                dbTable.Insert(dbEntry);
            }
            dbTable.Flush();
        }
        
        return true;
    }

    /// <summary>
    /// Change user password.
    /// </summary>
    public bool ChangePassword(string username, string newPassword)
    {
        var allUsers = _usersTable.GetAll();
        var user = allUsers.FirstOrDefault(u => u.Username == username);
        
        if (user == null)
        {
            return false;
        }

        user.PasswordHash = HashPassword(newPassword);
        _usersTable.Update(user);
        _usersTable.Flush();
        return true;
    }

    /// <summary>
    /// Delete user.
    /// </summary>
    public bool DeleteUser(string username)
    {
        var allUsers = _usersTable.GetAll();
        var users = allUsers.Where(u => u.Username == username).ToList();
        
        if (users.Count == 0)
        {
            return false;
        }

        foreach (var user in users)
            _usersTable.Delete(user);

        var dbTable = _systemDatabaseService.GetDbTable();
        var dbEntries = dbTable.GetAll().Where(db => db.User == username).ToList();
        foreach (var dbEntry in dbEntries)
        {
            dbTable.Delete(dbEntry);
        }

        return true;
    }

    /// <summary>
    /// Get all usernames.
    /// </summary>
    public List<string> GetAllUsers()
    {
        var allUsers = _usersTable.GetAll();
        return allUsers.Select(u => u.Username).Distinct().ToList();
    }

    /// <summary>
    /// Check if user exists.
    /// </summary>
    public bool UserExists(string username)
    {
        _usersTable.Reload();
        var allUsers = _usersTable.GetAll();
        return allUsers.Any(u => u.Username == username);
    }

    /// <summary>
    /// Restrict user access to localhost only.
    /// </summary>
    public bool RestrictToLocalhost(string username)
    {
        var allUsers = _usersTable.GetAll();
        var users = allUsers.Where(u => u.Username == username && u.Host == "%").ToList();
        
        if (users.Count == 0)
        {
            return false;
        }

        foreach (var user in users)
        {
            user.Host = "localhost";
            _usersTable.Update(user);
        }
        
        _usersTable.Flush();
        return true;
    }
}

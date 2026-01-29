using System.Text;
using ovudb.Network.Authentication;
using ovudb.SystemDatabase;

namespace ovudb.Tools;

/// <summary>
/// Utility for secure initial setup of OvuDB
/// Similar to mysql_secure_installation for MySQL
/// </summary>
public class OvuDbSecureInstallation
{
    private readonly string _dataDirectory;
    private readonly AuthenticationService _authService;
    private readonly SystemDatabaseService _systemDatabaseService;

    public OvuDbSecureInstallation(string dataDirectory = "data")
    {
        _dataDirectory = dataDirectory;
        _authService = new AuthenticationService(dataDirectory);
        
        var ovusysDirectory = Path.IsPathRooted(dataDirectory)
            ? Path.Combine(dataDirectory, "ovusys")
            : Path.Combine(Directory.GetCurrentDirectory(), dataDirectory, "ovusys");
        _systemDatabaseService = new SystemDatabaseService(ovusysDirectory);
    }

    /// <summary>
    /// Run the initial setup wizard
    /// </summary>
    public void Run()
    {
        Console.WriteLine();
        Console.WriteLine("==============================================================================");
        Console.WriteLine("Welcome to the OvuDB initial setup wizard!");
        Console.WriteLine("==============================================================================");
        Console.WriteLine();
        Console.WriteLine("This utility will help you configure security for your OvuDB installation.");
        Console.WriteLine("The following steps will be performed:");
        Console.WriteLine();
        Console.WriteLine("1. Create administrator user (admin)");
        Console.WriteLine("2. Set password for administrator");
        Console.WriteLine("3. Disallow remote login for administrator");
        Console.WriteLine("4. Remove test database");
        Console.WriteLine("5. Reload privilege tables");
        Console.WriteLine();

        // Create administrator user if it does not exist
        if (!_authService.UserExists("admin"))
        {
            Console.WriteLine("Creating administrator user...");
            _authService.CreateDefaultAdminUser();
            Console.WriteLine("✓ User 'admin' created with default password: admin");
            Console.WriteLine();
            
            // Verify the user was actually created
            if (!_authService.UserExists("admin"))
            {
                Console.WriteLine("ERROR: Failed to create administrator user.");
                return;
            }
        }

        Console.WriteLine("Press ENTER to continue or Ctrl+C to cancel...");
        Console.ReadLine();

        // 1. Set password for administrator
        SetRootPassword();

        // 2. Disallow remote login for administrator
        DisallowRemoteRootLogin();

        // 3. Remove test database
        RemoveTestDatabase();

        // 4. Reload privilege tables
        ReloadPrivilegeTables();

        Console.WriteLine();
        Console.WriteLine("==============================================================================");
        Console.WriteLine("Initial setup completed successfully!");
        Console.WriteLine("==============================================================================");
        Console.WriteLine();
        Console.WriteLine("All done! Your OvuDB installation is now more secure.");
        Console.WriteLine();
    }

    /// <summary>
    /// Set password for administrator
    /// </summary>
    private void SetRootPassword()
    {
        Console.WriteLine();
        Console.WriteLine("Setting password for administrator (admin)");
        Console.WriteLine("-------------------------------------------");

        if (!_authService.UserExists("admin"))
        {
            Console.WriteLine("User 'admin' not found. Skipping this step.");
            return;
        }

        Console.WriteLine("Enter new password for user 'admin':");
        var password = ReadPassword();

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Password cannot be empty. Skipping this step.");
            return;
        }

        Console.WriteLine("Repeat password:");
        var passwordConfirm = ReadPassword();

        if (password != passwordConfirm)
        {
            Console.WriteLine("Passwords do not match. Skipping this step.");
            return;
        }

        if (_authService.ChangePassword("admin", password))
        {
            Console.WriteLine("✓ Password for user 'admin' set successfully.");
        }
        else
        {
            Console.WriteLine("✗ Error setting password.");
        }
    }

    /// <summary>
    /// Disallow remote login for administrator
    /// </summary>
    private void DisallowRemoteRootLogin()
    {
        Console.WriteLine();
        Console.WriteLine("Disabling remote login for administrator");
        Console.WriteLine("----------------------------------------------");

        if (!_authService.UserExists("admin"))
        {
            Console.WriteLine("User 'admin' not found. Skipping this step.");
            return;
        }

        Console.Write("Restrict 'admin' login to localhost only? [Y/n]: ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (response == "n" || response == "no")
        {
            Console.WriteLine("Remote login for administrator allowed.");
            return;
        }

        if (_authService.RestrictToLocalhost("admin"))
        {
            Console.WriteLine("✓ Login for user 'admin' is now restricted to localhost only.");
        }
        else
        {
            Console.WriteLine("✗ Error restricting access.");
        }
    }

    /// <summary>
    /// Remove test database
    /// </summary>
    private void RemoveTestDatabase()
    {
        Console.WriteLine();
        Console.WriteLine("Removing test database");
        Console.WriteLine("------------------------------");

        // Check for test databases
        var testDbNames = new[] { "test", "Test", "TEST" };
        var foundTestDbs = new List<string>();

        foreach (var dbName in testDbNames)
        {
            var dbPath = Path.IsPathRooted(_dataDirectory)
                ? Path.Combine(_dataDirectory, dbName)
                : Path.Combine(Directory.GetCurrentDirectory(), _dataDirectory, dbName);

            if (Directory.Exists(dbPath))
            {
                foundTestDbs.Add(dbName);
            }
        }

        if (foundTestDbs.Count == 0)
        {
            Console.WriteLine("Test databases not found. Skipping this step.");
            return;
        }

        Console.WriteLine($"Found test databases: {string.Join(", ", foundTestDbs)}");
        Console.Write("Remove test databases? [Y/n]: ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (response == "n" || response == "no")
        {
            Console.WriteLine("Skipping removal of test databases.");
            return;
        }

        foreach (var dbName in foundTestDbs)
        {
            try
            {
                var dbPath = Path.IsPathRooted(_dataDirectory)
                    ? Path.Combine(_dataDirectory, dbName)
                    : Path.Combine(Directory.GetCurrentDirectory(), _dataDirectory, dbName);

                if (Directory.Exists(dbPath))
                {
                    Directory.Delete(dbPath, true);
                    Console.WriteLine($"✓ Test database '{dbName}' removed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error removing database '{dbName}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Reload privilege tables
    /// </summary>
    private void ReloadPrivilegeTables()
    {
        Console.WriteLine();
        Console.WriteLine("Reloading privilege tables");
        Console.WriteLine("------------------------------");
        Console.WriteLine("Privilege tables are already up to date (changes apply immediately).");
        Console.WriteLine("✓ Privilege tables are ready.");
    }

    /// <summary>
    /// Read password from console (without displaying characters)
    /// </summary>
    private string ReadPassword()
    {
        var password = new StringBuilder();
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(true);

            if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
            else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
        }
        while (key.Key != ConsoleKey.Enter);

        Console.WriteLine();
        return password.ToString();
    }
}

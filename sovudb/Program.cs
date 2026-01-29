using ovudb.Configuration;
using ovudb.Network;
using ovudb.Network.Authentication;
using ovudb.Tools;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("=== OvuDB Server ===");
        
        // Load configuration from file
        // First argument, if not a flag, is used as path to config file
        var configPath = "ovudbc.yml";
        if (args.Length > 0 && !args[0].StartsWith("-"))
        {
            configPath = args[0];
        }
        var config = ConfigLoader.LoadFromFile(configPath);

        // Check system database presence and integrity
        var authService = new ovudb.Network.Authentication.AuthenticationService(config.DataDirectory);
        var needsSetup = !authService.IsSystemDatabaseValid();
        
        if (needsSetup)
        {
            Console.WriteLine();
            Console.WriteLine("==============================================================================");
            Console.WriteLine("Missing or corrupted system database detected!");
            Console.WriteLine("==============================================================================");
            Console.WriteLine();
            Console.WriteLine("Initial OvuDB setup is required.");
            Console.WriteLine("Starting initial setup wizard...");
            Console.WriteLine();
            
            var installer = new OvuDbSecureInstallation(config.DataDirectory);
            installer.Run();
            
            // Recreate AuthenticationService and verify setup completed successfully
            authService = new ovudb.Network.Authentication.AuthenticationService(config.DataDirectory);
            if (!authService.IsSystemDatabaseValid())
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: Failed to complete initial setup.");
                Console.WriteLine("Check data directory permissions and try again.");
                return;
            }
            
            Console.WriteLine();
            Console.WriteLine("Initial setup complete. Starting server...");
            Console.WriteLine();
        }
        
        Console.WriteLine($"Configuration loaded from: {configPath}");
        Console.WriteLine($"Port: {config.Port}");
        Console.WriteLine($"Data directory: {config.DataDirectory}");
        Console.WriteLine($"Max connections: {config.MaxConnections}");
        Console.WriteLine($"Idle connection timeout: {config.IdleTimeoutMinutes} min");
        Console.WriteLine($"Buffer pool size: {config.BufferPoolSize} pages");
        Console.WriteLine($"Page size: {config.PageSize} bytes");
        Console.WriteLine($"Query cache: {config.QueryCacheMaxEntries} entries, TTL: {config.QueryCacheTtlMinutes} min");
        Console.WriteLine("\nTo connect use: username=admin, password=admin");
        Console.WriteLine("Press Ctrl+C to stop the server...\n");

        var server = new OvuDbServer(
            port: config.Port, 
            dataDirectory: config.DataDirectory,
            maxConnections: config.MaxConnections,
            idleTimeoutMinutes: config.IdleTimeoutMinutes
        );
        
        var serverTask = Task.Run(async () =>
        {
            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
        });

        // Wait for server to finish
        try
        {
            await serverTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            server.Stop();
            Console.WriteLine("\nServer stopped");
        }
    }
}
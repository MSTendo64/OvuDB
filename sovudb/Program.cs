using ovudb.Configuration;
using ovudb.Network;
using ovudb.Network.Authentication;
using ovudb.Network.MySQL;
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
        Console.WriteLine($"OvuDB port: {config.Port}");
        if (config.MySqlPort.HasValue)
        {
            Console.WriteLine($"MySQL-compatible port: {config.MySqlPort}");
        }
        else
        {
            Console.WriteLine("MySQL-compatible port: not configured (mysqlPort not set or null)");
        }
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

        MySqlServer? mySqlServer = null;
        if (config.MySqlPort.HasValue)
        {
            try
            {
                mySqlServer = new MySqlServer(
                    port: config.MySqlPort.Value,
                    dataDirectory: config.DataDirectory,
                    maxConnections: config.MaxConnections,
                    idleTimeoutMinutes: config.IdleTimeoutMinutes
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to start MySQL-compatible server: {ex.Message}");
            }
        }
        
        // Setup cancellation token for graceful shutdown
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\nShutting down servers...");
        };

        var serverTask = Task.Run(async () =>
        {
            try
            {
                await server.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OvuDB server error: {ex.Message}");
            }
        });

        Task? mySqlServerTask = null;
        if (mySqlServer != null)
        {
            mySqlServerTask = Task.Run(async () =>
            {
                try
                {
                    await mySqlServer.StartAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"MySQL server error: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            });
        }
        else
        {
            Console.WriteLine("MySQL-compatible server is disabled (mysqlPort not set in config)");
        }

        // Wait for cancellation signal (Ctrl+C)
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            cts.Cancel();
            server.Stop();
            mySqlServer?.Stop();
            
            // Wait a bit for tasks to complete
            try
            {
                await Task.WhenAll(
                    serverTask,
                    mySqlServerTask ?? Task.CompletedTask
                ).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore timeout
            }
            
            Console.WriteLine("\nServers stopped");
        }
    }
}
using ovudb.Network;

namespace ovudb.Network;

/// <summary>
/// Example usage of OvuDB server
/// </summary>
public static class OvuDbServerExample
{
    public static async Task RunExample()
    {
        var server = new OvuDbServer(port: 47015, dataDirectory: "data");
        
        // Start server in separate task
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

        Console.WriteLine("Server started. Press any key to stop...");
        Console.ReadKey();

        server.Stop();
        await serverTask;
    }
}

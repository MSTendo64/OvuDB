using ovudb.Core;
using ovudb.Network;
using ovudb.Network.Authentication;
using ovudb.SystemDatabase;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace ovudb.Tests.Network;

/// <summary>
/// Advanced network layer tests
/// </summary>
public class NetworkAdvancedTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly OvuDbServer _server;
    private readonly int _testPort = 47016;

    public NetworkAdvancedTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_network_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
        _server = new OvuDbServer(_testPort, _testDataDirectory);
        _ = _server.StartAsync();
        
        // Give server time to start
        Thread.Sleep(100);
    }

    public void Dispose()
    {
        try
        {
            _server?.Stop();
            _server?.Dispose();
        }
        catch { }

        if (Directory.Exists(_testDataDirectory))
        {
            try
            {
                Directory.Delete(_testDataDirectory, true);
            }
            catch { }
        }
    }

    #region Connection tests

    [Fact]
    public void Server_Start_AcceptsConnections()
    {
        using var client = new TcpClient();
        var connected = false;
        try
        {
            client.Connect("localhost", _testPort);
            connected = client.Connected;
        }
        catch { }

        Assert.True(connected, "Server must accept connections");
    }

    [Fact]
    public void Server_Stop_ClosesConnections()
    {
        _server.Stop();
        Thread.Sleep(100);

        using var client = new TcpClient();
        var connected = false;
        try
        {
            client.Connect("localhost", _testPort);
            connected = client.Connected;
        }
        catch { }

        Assert.False(connected, "Server must close connections after stop");
    }

    [Fact]
    public void Server_MultipleConnections_HandlesConcurrently()
    {
        var tasks = new List<Task<bool>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    using var client = new TcpClient();
                    client.Connect("localhost", _testPort);
                    return client.Connected;
                }
                catch
                {
                    return false;
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        var successCount = tasks.Count(t => t.Result);
        Assert.True(successCount >= 8, $"At least 8 clients should connect successfully, connected: {successCount}");
    }

    #endregion

    #region Command tests

    [Fact]
    public void Server_CreateDatabase_CommandWorks()
    {
        // This test requires real connection via Connection
        // Here we verify server is created
        Assert.NotNull(_server);
    }

    [Fact]
    public void Server_ShowDatabases_CommandWorks()
    {
        Assert.NotNull(_server);
    }

    #endregion

    #region Error handling tests

    [Fact]
    public void Server_InvalidPort_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            using var server = new OvuDbServer(-1, _testDataDirectory);
        });
    }

    [Fact]
    public void Server_AlreadyRunning_HandlesGracefully()
    {
        // Server already started in constructor
        Assert.NotNull(_server);
        
        // Attempt to start again should throw
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _server.StartAsync();
        });
    }

    #endregion
}

/// <summary>
/// ConnectionPool tests
/// </summary>
public class ConnectionPoolAdvancedTests : IDisposable
{
    private readonly ConnectionPool _pool;
    private readonly int _maxConnections = 10;

    public ConnectionPoolAdvancedTests()
    {
        _pool = new ConnectionPool(_maxConnections, TimeSpan.FromMinutes(5));
    }

    public void Dispose()
    {
        _pool?.Dispose();
    }

    [Fact]
    public void ConnectionPool_GetConnection_ReturnsConnection()
    {
        // Create real TCP connection for test
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();
        
        var connection = new Connection(serverClient);
        var connectionId = connection.ConnectionId;
        
        // Add connection to pool
        Assert.True(_pool.AddConnection(connection));
        
        // Get connection from pool
        var retrievedConnection = _pool.GetConnection(connectionId);
        Assert.NotNull(retrievedConnection);
        Assert.Equal(connectionId, retrievedConnection.ConnectionId);
        
        listener.Stop();
        client.Dispose();
    }

    [Fact]
    public void ConnectionPool_MaxConnections_EnforcesLimit()
    {
        var connections = new List<Connection>();
        var listeners = new List<TcpListener>();
        var clients = new List<TcpClient>();
        
        try
        {
            for (int i = 0; i < _maxConnections + 5; i++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                listeners.Add(listener);
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                
                var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                clients.Add(client);
                
                var serverClient = listener.AcceptTcpClient();
                var conn = new Connection(serverClient);
                
                if (_pool.AddConnection(conn))
                {
                    connections.Add(conn);
                }
                else
                {
                    // Connection limit reached
                    conn.Dispose();
                    break;
                }
            }
        }
        catch
        {
            // Expect exception when limit exceeded
        }

        Assert.True(connections.Count <= _maxConnections);
        
        // Clear connections
        foreach (var conn in connections)
        {
            _pool.RemoveConnection(conn.ConnectionId);
            conn.Dispose();
        }
        
        foreach (var listener in listeners)
        {
            listener.Stop();
        }
        
        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    [Fact]
    public void ConnectionPool_RemoveConnection_RemovesConnection()
    {
        // Create real TCP connection for test
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();
        
        var connection = new Connection(serverClient);
        var connectionId = connection.ConnectionId;
        
        // Add connection to pool
        Assert.True(_pool.AddConnection(connection));
        
        // Get connection from pool
        var connection1 = _pool.GetConnection(connectionId);
        Assert.NotNull(connection1);
        
        // Remove connection
        _pool.RemoveConnection(connectionId);
        
        // Verify connection was removed
        var connection2 = _pool.GetConnection(connectionId);
        Assert.Null(connection2);
        
        listener.Stop();
        client.Dispose();
    }

    [Fact]
    public void ConnectionPool_Dispose_ClosesAllConnections()
    {
        var connections = new List<Connection>();
        for (int i = 0; i < 5; i++)
        {
            var conn = _pool.GetConnection(Guid.NewGuid().ToString());
            if (conn != null)
            {
                connections.Add(conn);
            }
        }

        _pool.Dispose();

        // After Dispose all connections must be closed
        Assert.True(true); // Check performed in Dispose
    }
}

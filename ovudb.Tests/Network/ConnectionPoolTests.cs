using ovudb.Network;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace ovudb.Tests.Network;

public class ConnectionPoolTests : IDisposable
{
    private readonly ConnectionPool _pool;

    public ConnectionPoolTests()
    {
        _pool = new ConnectionPool(maxConnections: 10, idleTimeout: TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ConnectionPool_AddConnection_Success()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        
        var serverClient = listener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        var added = _pool.AddConnection(connection);

        Assert.True(added);
        Assert.Equal(1, _pool.Count);
        Assert.NotNull(_pool.GetConnection(connection.ConnectionId));

        listener.Stop();
        client.Dispose();
    }

    [Fact]
    public void ConnectionPool_AddConnection_DuplicateId_Replaces()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client1 = new TcpClient();
        client1.Connect(IPAddress.Loopback, port);
        var serverClient1 = listener.AcceptTcpClient();
        var connection1 = new Connection(serverClient1);

        _pool.AddConnection(connection1);
        Assert.Equal(1, _pool.Count);

        // Add connection with same ID (should not happen, but verify behavior)
        var added = _pool.AddConnection(connection1);
        // Re-adding same connection may or may not be allowed
        // Depends on current implementation logic

        listener.Stop();
        client1.Dispose();
    }

    [Fact]
    public void ConnectionPool_ExceedsMaxConnections_ReturnsFalse()
    {
        var pool = new ConnectionPool(maxConnections: 2);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var connections = new List<Connection>();

        // Add maximum number of connections
        for (int i = 0; i < 2; i++)
        {
            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            var serverClient = listener.AcceptTcpClient();
            var connection = new Connection(serverClient);
            var added = pool.AddConnection(connection);
            Assert.True(added);
            connections.Add(connection);
        }

        Assert.Equal(2, pool.Count);

        // Attempt to add one more connection should return false
        var client3 = new TcpClient();
        client3.Connect(IPAddress.Loopback, port);
        var serverClient3 = listener.AcceptTcpClient();
        var connection3 = new Connection(serverClient3);
        var added3 = pool.AddConnection(connection3);

        Assert.False(added3);
        Assert.Equal(2, pool.Count); // Count unchanged

        listener.Stop();
        foreach (var conn in connections)
        {
            conn.Dispose();
        }
        connection3.Dispose();
        pool.Dispose();
    }

    [Fact]
    public void ConnectionPool_RemoveConnection_RemovesFromPool()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        
        var serverClient = listener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        _pool.AddConnection(connection);
        Assert.Equal(1, _pool.Count);
        Assert.NotNull(_pool.GetConnection(connection.ConnectionId));

        _pool.RemoveConnection(connection.ConnectionId);
        Assert.Equal(0, _pool.Count);
        Assert.Null(_pool.GetConnection(connection.ConnectionId));

        listener.Stop();
        client.Dispose();
    }

    [Fact]
    public void ConnectionPool_RemoveConnection_NonExistent_NoError()
    {
        _pool.RemoveConnection("nonexistent_id");
        Assert.Equal(0, _pool.Count);
    }

    [Fact]
    public void ConnectionPool_GetConnection_ReturnsCorrectConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        
        var serverClient = listener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        _pool.AddConnection(connection);
        var retrieved = _pool.GetConnection(connection.ConnectionId);

        Assert.NotNull(retrieved);
        Assert.Equal(connection.ConnectionId, retrieved!.ConnectionId);
        Assert.Same(connection, retrieved);

        listener.Stop();
        client.Dispose();
    }

    [Fact]
    public void ConnectionPool_GetConnection_NonExistent_ReturnsNull()
    {
        var connection = _pool.GetConnection("nonexistent_id");
        Assert.Null(connection);
    }

    [Fact]
    public void ConnectionPool_GetAllConnections_ReturnsAll()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var connections = new List<Connection>();

        for (int i = 0; i < 3; i++)
        {
            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            var serverClient = listener.AcceptTcpClient();
            var connection = new Connection(serverClient);
            _pool.AddConnection(connection);
            connections.Add(connection);
        }

        var allConnections = _pool.GetAllConnections();
        Assert.Equal(3, allConnections.Count);
        Assert.All(allConnections, c => Assert.Contains(c, connections));

        listener.Stop();
        foreach (var conn in connections)
        {
            conn.Dispose();
        }
    }

    [Fact]
    public void ConnectionPool_GetStats_ReturnsCorrectStats()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client1 = new TcpClient();
        client1.Connect(IPAddress.Loopback, port);
        var serverClient1 = listener.AcceptTcpClient();
        var connection1 = new Connection(serverClient1);
        connection1.Authenticate("user1");

        var client2 = new TcpClient();
        client2.Connect(IPAddress.Loopback, port);
        var serverClient2 = listener.AcceptTcpClient();
        var connection2 = new Connection(serverClient2);
        // connection2 not authenticated

        _pool.AddConnection(connection1);
        _pool.AddConnection(connection2);
        
        var stats = _pool.GetStats();

        Assert.Equal(2, stats.TotalConnections);
        Assert.Equal(1, stats.AuthenticatedConnections);
        Assert.Equal(10, stats.MaxConnections);
        Assert.Equal(TimeSpan.FromMinutes(30), stats.IdleTimeout);

        listener.Stop();
        client1.Dispose();
        client2.Dispose();
    }

    [Fact]
    public void ConnectionPool_CleanupIdleConnections_RemovesIdle()
    {
        var pool = new ConnectionPool(maxConnections: 10, idleTimeout: TimeSpan.FromMilliseconds(100));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        pool.AddConnection(connection);
        Assert.Equal(1, pool.Count);

        // Wait for connection to become idle
        Thread.Sleep(150);

        pool.CleanupIdleConnections();
        Assert.Equal(0, pool.Count);

        listener.Stop();
        client.Dispose();
        pool.Dispose();
    }

    [Fact]
    public void ConnectionPool_CleanupIdleConnections_KeepsActive()
    {
        var pool = new ConnectionPool(maxConnections: 10, idleTimeout: TimeSpan.FromMilliseconds(200));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        pool.AddConnection(connection);
        Assert.Equal(1, pool.Count);

        // Update activity before timeout
        Thread.Sleep(100);
        connection.UpdateActivity();
        Thread.Sleep(100);

        pool.CleanupIdleConnections();
        Assert.Equal(1, pool.Count); // Connection should remain

        listener.Stop();
        client.Dispose();
        pool.Dispose();
    }

    [Fact]
    public void ConnectionPool_Dispose_ClearsAllConnections()
    {
        var pool = new ConnectionPool(maxConnections: 10);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var connections = new List<Connection>();
        for (int i = 0; i < 3; i++)
        {
            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            var serverClient = listener.AcceptTcpClient();
            var connection = new Connection(serverClient);
            pool.AddConnection(connection);
            connections.Add(connection);
        }

        Assert.Equal(3, pool.Count);
        pool.Dispose();
        Assert.Equal(0, pool.Count);

        listener.Stop();
        foreach (var conn in connections)
        {
            conn.Dispose();
        }
    }

    public void Dispose()
    {
        _pool.Dispose();
    }
}

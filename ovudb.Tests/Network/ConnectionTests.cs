using ovudb.Network;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ovudb.Tests.Network;

public class ConnectionTests : IDisposable
{
    private TcpListener? _testListener;
    private TcpClient? _testClient;

    [Fact]
    public void Connection_CreatesWithUniqueId()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        Assert.NotNull(connection.ConnectionId);
        Assert.Equal(8, connection.ConnectionId.Length); // ID should be 8 chars
        Assert.False(connection.IsAuthenticated);
        Assert.True(connection.IsConnected);
        Assert.True(connection.ConnectedAt <= DateTime.UtcNow);
        Assert.True(connection.LastActivity <= DateTime.UtcNow);

        connection.Dispose();
    }

    [Fact]
    public void Connection_Authenticate_SetsUsername()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        Assert.False(connection.IsAuthenticated);
        Assert.Null(connection.Username);

        connection.Authenticate("testuser");

        Assert.True(connection.IsAuthenticated);
        Assert.Equal("testuser", connection.Username);

        connection.Dispose();
    }

    [Fact]
    public void Connection_UpdateActivity_UpdatesTimestamp()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        var initialActivity = connection.LastActivity;
        Thread.Sleep(10);
        connection.UpdateActivity();
        var updatedActivity = connection.LastActivity;

        Assert.True(updatedActivity > initialActivity);

        connection.Dispose();
    }

    [Fact]
    public async Task Connection_SendResponse_Works()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        var response = new Response
        {
            Success = true,
            Data = new { message = "test", value = 42 }
        };
        await connection.SendResponseAsync(response);

        var stream = _testClient.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync();

        Assert.NotNull(line);
        var receivedResponse = JsonSerializer.Deserialize<Response>(line, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(receivedResponse);
        Assert.True(receivedResponse.Success);
        Assert.Null(receivedResponse.Error);
        Assert.NotNull(receivedResponse.Data);

        connection.Dispose();
    }

    [Fact]
    public async Task Connection_SendError_Works()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        await connection.SendErrorAsync("Test error message");

        var stream = _testClient.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync();

        Assert.NotNull(line);
        var receivedResponse = JsonSerializer.Deserialize<Response>(line, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(receivedResponse);
        Assert.False(receivedResponse.Success);
        Assert.Equal("Test error message", receivedResponse.Error);

        connection.Dispose();
    }

    [Fact]
    public async Task Connection_SendSuccess_Works()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        var testData = new { result = "success", count = 5 };
        await connection.SendSuccessAsync(testData);

        var stream = _testClient.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync();

        Assert.NotNull(line);
        var receivedResponse = JsonSerializer.Deserialize<Response>(line, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(receivedResponse);
        Assert.True(receivedResponse.Success);
        Assert.Null(receivedResponse.Error);
        Assert.NotNull(receivedResponse.Data);

        connection.Dispose();
    }

    [Fact]
    public async Task Connection_ReceiveRequest_Works()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        // Send request from client
        var request = new Request
        {
            Command = "TEST",
            Parameters = new Dictionary<string, object> { ["key"] = "value" },
            Database = "testdb",
            Table = "testtable"
        };

        var stream = _testClient.GetStream();
        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));

        // Read request on server
        var receivedRequest = await connection.ReceiveRequestAsync();

        Assert.NotNull(receivedRequest);
        Assert.Equal("TEST", receivedRequest.Command);
        Assert.NotNull(receivedRequest.Parameters);
        Assert.Equal("value", receivedRequest.Parameters["key"]?.ToString());
        Assert.Equal("testdb", receivedRequest.Database);
        Assert.Equal("testtable", receivedRequest.Table);

        connection.Dispose();
    }

    [Fact]
    public async Task Connection_ReceiveRequest_NullOnEmptyLine()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        // Close connection from client
        _testClient.Close();

        // Read attempt should return null
        var request = await connection.ReceiveRequestAsync();
        Assert.Null(request);

        connection.Dispose();
    }

    [Fact]
    public void Connection_Dispose_ClosesConnection()
    {
        _testListener = new TcpListener(IPAddress.Loopback, 0);
        _testListener.Start();
        var port = ((IPEndPoint)_testListener.LocalEndpoint).Port;
        
        _testClient = new TcpClient();
        _testClient.Connect(IPAddress.Loopback, port);
        
        var serverClient = _testListener.AcceptTcpClient();
        var connection = new Connection(serverClient);

        Assert.True(connection.IsConnected);
        connection.Dispose();
        Assert.False(connection.IsConnected);
    }

    public void Dispose()
    {
        _testClient?.Dispose();
        _testListener?.Stop();
    }
}

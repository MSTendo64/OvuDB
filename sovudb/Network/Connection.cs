using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ovudb.Network;

/// <summary>
/// Represents client connection to database
/// </summary>
public class Connection : IDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly string _connectionId;
    private DateTime _connectedAt;
    private DateTime _lastActivity;
    private bool _isAuthenticated;
    private string? _username;

    public Connection(TcpClient tcpClient)
    {
        _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        _stream = _tcpClient.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8);
        _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
        _connectionId = Guid.NewGuid().ToString("N")[..8];
        _connectedAt = DateTime.UtcNow;
        _lastActivity = DateTime.UtcNow;
        _isAuthenticated = false;
    }

    public string ConnectionId => _connectionId;
    public DateTime ConnectedAt => _connectedAt;
    public DateTime LastActivity => _lastActivity;
    public bool IsAuthenticated => _isAuthenticated;
    public string? Username => _username;
    public bool IsConnected => _tcpClient.Connected;

    /// <summary>
    /// Authenticate connection
    /// </summary>
    public void Authenticate(string username)
    {
        _isAuthenticated = true;
        _username = username;
        UpdateActivity();
    }

    /// <summary>
    /// Update last activity time
    /// </summary>
    public void UpdateActivity()
    {
        _lastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Send response to client
    /// </summary>
    public async Task SendResponseAsync(Response response, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            await _writer.WriteLineAsync(json);
            UpdateActivity();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error sending response: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get request from client
    /// </summary>
    public async Task<Request?> ReceiveRequestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var line = await _reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }

            UpdateActivity();
            
            var request = JsonSerializer.Deserialize<Request>(line, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return request;
        }
        catch (IOException)
        {
            // Connection closed by client
            return null;
        }
        catch (SocketException)
        {
            // Connection closed
            return null;
        }
        catch (ObjectDisposedException)
        {
            // Stream was closed
            return null;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error receiving request: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Send error message
    /// </summary>
    public async Task SendErrorAsync(string error, CancellationToken cancellationToken = default)
    {
        var errorResponse = new Response
        {
            Success = false,
            Error = error,
            Data = null
        };
        await SendResponseAsync(errorResponse, cancellationToken);
    }

    /// <summary>
    /// Send success response
    /// </summary>
    public async Task SendSuccessAsync(object? data = null, CancellationToken cancellationToken = default)
    {
        var successResponse = new Response
        {
            Success = true,
            Error = null,
            Data = data
        };
        await SendResponseAsync(successResponse, cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            _reader?.Dispose();
            _writer?.Dispose();
            _stream?.Dispose();
            _tcpClient?.Dispose();
        }
        catch
        {
            // Ignore errors on close
        }
    }
}

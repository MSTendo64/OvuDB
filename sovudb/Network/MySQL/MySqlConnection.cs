using System.Net.Sockets;
using System.Text;
using ovudb.Network.Authentication;

namespace ovudb.Network.MySQL;

/// <summary>
/// MySQL protocol connection handler
/// </summary>
public class MySqlConnection : IDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly MySqlPacketReader _reader;
    private readonly MySqlPacketWriter _writer;
    private readonly string _connectionId;
    private bool _isAuthenticated;
    private string? _username;
    private string? _currentDatabase;
    private DateTime _lastActivity;
    private readonly Encoding _encoding = Encoding.UTF8;

    public MySqlConnection(TcpClient tcpClient)
    {
        _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        _stream = _tcpClient.GetStream();
        _reader = new MySqlPacketReader(_stream);
        _writer = new MySqlPacketWriter(_stream);
        _connectionId = Guid.NewGuid().ToString("N")[..8];
        _lastActivity = DateTime.UtcNow;
        _isAuthenticated = false;
    }

    public string ConnectionId => _connectionId;
    public bool IsAuthenticated => _isAuthenticated;
    public string? Username => _username;
    public string? CurrentDatabase => _currentDatabase;
    public bool IsConnected => _tcpClient.Connected;
    public DateTime LastActivity => _lastActivity;

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
    /// Set current database
    /// </summary>
    public void SetDatabase(string? database)
    {
        _currentDatabase = database;
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
    /// Read a MySQL packet
    /// </summary>
    public async Task<byte[]> ReadPacketAsync(CancellationToken cancellationToken = default)
    {
        UpdateActivity();
        return await _reader.ReadPacketAsync(cancellationToken);
    }

    /// <summary>
    /// Write a MySQL packet
    /// </summary>
    public async Task WritePacketAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        UpdateActivity();
        Console.WriteLine($"[MySQL Connection] Writing packet: {data.Length} bytes");
        await _writer.WritePacketAsync(data, cancellationToken);
        Console.WriteLine($"[MySQL Connection] Packet written successfully");
    }

    /// <summary>
    /// Flush the network stream
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _stream.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            _stream?.Dispose();
            _tcpClient?.Dispose();
        }
        catch
        {
            // Ignore errors on close
        }
    }
}


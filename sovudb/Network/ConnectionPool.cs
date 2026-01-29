namespace ovudb.Network;

/// <summary>
/// Connection pool for managing client connections
/// </summary>
public class ConnectionPool : IDisposable
{
    private readonly Dictionary<string, Connection> _connections = new();
    private readonly object _lock = new();
    private readonly int _maxConnections;
    private readonly TimeSpan _idleTimeout;

    public ConnectionPool(int maxConnections = 100, TimeSpan? idleTimeout = null)
    {
        _maxConnections = maxConnections;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Add connection to pool
    /// </summary>
    public bool AddConnection(Connection connection)
    {
        lock (_lock)
        {
            if (_connections.Count >= _maxConnections)
            {
                return false;
            }

            _connections[connection.ConnectionId] = connection;
            return true;
        }
    }

    /// <summary>
    /// Remove connection from pool
    /// </summary>
    public void RemoveConnection(string connectionId)
    {
        lock (_lock)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                _connections.Remove(connectionId);
                connection?.Dispose();
            }
        }
    }

    /// <summary>
    /// Get connection by ID
    /// </summary>
    public Connection? GetConnection(string connectionId)
    {
        lock (_lock)
        {
            _connections.TryGetValue(connectionId, out var connection);
            return connection;
        }
    }

    /// <summary>
    /// Get all active connections
    /// </summary>
    public List<Connection> GetAllConnections()
    {
        lock (_lock)
        {
            return _connections.Values.ToList();
        }
    }

    /// <summary>
    /// Clear inactive connections
    /// </summary>
    public void CleanupIdleConnections()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var toRemove = _connections.Values
                .Where(c => !c.IsConnected || (now - c.LastActivity) > _idleTimeout)
                .Select(c => c.ConnectionId)
                .ToList();

            foreach (var connectionId in toRemove)
            {
                RemoveConnection(connectionId);
            }
        }
    }

    /// <summary>
    /// Get count of active connections
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _connections.Count;
            }
        }
    }

    /// <summary>
    /// Get pool statistics
    /// </summary>
    public ConnectionPoolStats GetStats()
    {
        lock (_lock)
        {
            var connections = _connections.Values.ToList();
            return new ConnectionPoolStats
            {
                TotalConnections = connections.Count,
                AuthenticatedConnections = connections.Count(c => c.IsAuthenticated),
                MaxConnections = _maxConnections,
                IdleTimeout = _idleTimeout
            };
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var connection in _connections.Values)
            {
                connection?.Dispose();
            }
            _connections.Clear();
        }
    }
}

/// <summary>
/// Connection pool statistics
/// </summary>
public class ConnectionPoolStats
{
    public int TotalConnections { get; set; }
    public int AuthenticatedConnections { get; set; }
    public int MaxConnections { get; set; }
    public TimeSpan IdleTimeout { get; set; }
}

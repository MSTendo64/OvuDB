namespace ovudb.Configuration;

/// <summary>
/// OvuDB server configuration
/// </summary>
public class ServerConfig
{
    /// <summary>
    /// Server port
    /// </summary>
    public int Port { get; set; } = 47015;

    /// <summary>
    /// Data directory
    /// </summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>
    /// Max concurrent connections
    /// </summary>
    public int MaxConnections { get; set; } = 100;

    /// <summary>
    /// Idle connection timeout (minutes)
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Buffer pool size (number of pages)
    /// </summary>
    public int BufferPoolSize { get; set; } = 1000;

    /// <summary>
    /// Page size in bytes
    /// </summary>
    public int PageSize { get; set; } = 8192;

    /// <summary>
    /// Max query cache entries
    /// </summary>
    public int QueryCacheMaxEntries { get; set; } = 100;

    /// <summary>
    /// Query cache TTL (minutes)
    /// </summary>
    public int QueryCacheTtlMinutes { get; set; } = 5;
}

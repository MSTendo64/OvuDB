namespace ovudb.Storage;

/// <summary>
/// Cache for table metadata
/// </summary>
public class MetadataCache
{
    private readonly Dictionary<string, Dictionary<string, object>> _tableSchemas = new();
    private readonly Dictionary<string, Dictionary<string, object>> _tableMetadata = new();
    private readonly object _lockObject = new();

    /// <summary>
    /// Get table schema from cache
    /// </summary>
    public Dictionary<string, object>? GetSchema(string tableName)
    {
        lock (_lockObject)
        {
            return _tableSchemas.TryGetValue(tableName, out var schema) ? schema : null;
        }
    }

    /// <summary>
    /// Put table schema into cache
    /// </summary>
    public void PutSchema(string tableName, Dictionary<string, object> schema)
    {
        lock (_lockObject)
        {
            _tableSchemas[tableName] = schema;
        }
    }

    /// <summary>
    /// Get table metadata from cache
    /// </summary>
    public Dictionary<string, object>? GetMetadata(string tableName)
    {
        lock (_lockObject)
        {
            return _tableMetadata.TryGetValue(tableName, out var metadata) ? metadata : null;
        }
    }

    /// <summary>
    /// Put table metadata into cache
    /// </summary>
    public void PutMetadata(string tableName, Dictionary<string, object> metadata)
    {
        lock (_lockObject)
        {
            _tableMetadata[tableName] = metadata;
        }
    }

    /// <summary>
    /// Remove table from cache
    /// </summary>
    public void Invalidate(string tableName)
    {
        lock (_lockObject)
        {
            _tableSchemas.Remove(tableName);
            _tableMetadata.Remove(tableName);
        }
    }

    /// <summary>
    /// Clear entire cache
    /// </summary>
    public void Clear()
    {
        lock (_lockObject)
        {
            _tableSchemas.Clear();
            _tableMetadata.Clear();
        }
    }

    /// <summary>
    /// Get count of cached tables
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lockObject)
            {
                return _tableSchemas.Count;
            }
        }
    }
}

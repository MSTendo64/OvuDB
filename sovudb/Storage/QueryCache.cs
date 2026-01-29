using System.Security.Cryptography;
using System.Text;

namespace ovudb.Storage;

/// <summary>
/// Cache for query results
/// </summary>
public class QueryCache
{
    private readonly Dictionary<string, CachedQueryResult> _cache = new();
    private readonly int _maxEntries;
    private readonly TimeSpan _defaultTtl;
    private readonly object _lockObject = new();

    /// <summary>
    /// Cached query result
    /// </summary>
    private class CachedQueryResult
    {
        public object Data { get; set; } = null!;
        public string TableName { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime LastAccessed { get; set; }
        public int AccessCount { get; set; }
    }

    public QueryCache(int maxEntries = 100, TimeSpan? defaultTtl = null)
    {
        _maxEntries = maxEntries;
        _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Get query result from cache
    /// </summary>
    public T? Get<T>(string queryKey)
    {
        lock (_lockObject)
        {
            if (!_cache.TryGetValue(queryKey, out var cached))
            {
                return default;
            }

            // Check expiration
            if (DateTime.UtcNow > cached.ExpiresAt)
            {
                _cache.Remove(queryKey);
                return default;
            }

            cached.LastAccessed = DateTime.UtcNow;
            cached.AccessCount++;

            if (cached.Data is T result)
            {
                return result;
            }

            return default;
        }
    }

    /// <summary>
    /// Put query result into cache
    /// </summary>
    public void Put<T>(string queryKey, T data, TimeSpan? ttl = null)
    {
        Put(queryKey, data, string.Empty, ttl);
    }

    /// <summary>
    /// Put query result into cache with table name
    /// </summary>
    public void Put<T>(string queryKey, T data, string tableName, TimeSpan? ttl = null)
    {
        lock (_lockObject)
        {
            // If limit reached, remove old entries
            if (_cache.Count >= _maxEntries && !_cache.ContainsKey(queryKey))
            {
                EvictOldEntries();
            }

            _cache[queryKey] = new CachedQueryResult
            {
                Data = data!,
                TableName = tableName,
                ExpiresAt = DateTime.UtcNow + (ttl ?? _defaultTtl),
                LastAccessed = DateTime.UtcNow,
                AccessCount = 1
            };
        }
    }

    /// <summary>
    /// Remove entry from cache
    /// </summary>
    public void Remove(string queryKey)
    {
        lock (_lockObject)
        {
            _cache.Remove(queryKey);
        }
    }

    /// <summary>
    /// Clear all entries for table
    /// </summary>
    public void InvalidateTable(string tableName)
    {
        lock (_lockObject)
        {
            var keysToRemove = _cache
                .Where(kvp => string.Equals(kvp.Value.TableName, tableName, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }
    }

    /// <summary>
    /// Clear entire cache
    /// </summary>
    public void Clear()
    {
        lock (_lockObject)
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// Remove expired entries
    /// </summary>
    private void EvictOldEntries()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.Remove(key);
        }

        // If still over capacity, remove least used
        if (_cache.Count >= _maxEntries)
        {
            var toRemove = _cache
                .OrderBy(kvp => kvp.Value.LastAccessed)
                .ThenBy(kvp => kvp.Value.AccessCount)
                .Take(_cache.Count - _maxEntries + 1)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _cache.Remove(key);
            }
        }
    }

    /// <summary>
    /// Generate key for query
    /// </summary>
    public static string GenerateKey(string tableName, string query, Dictionary<string, object>? parameters = null)
    {
        var sb = new StringBuilder();
        sb.Append($"[{tableName}]");
        sb.Append($"Query:{query}");
        
        if (parameters != null && parameters.Count > 0)
        {
            var sortedParams = parameters.OrderBy(kvp => kvp.Key);
            foreach (var param in sortedParams)
            {
                sb.Append($"|{param.Key}:{param.Value}");
            }
        }

        // Hash to get fixed-length key
        var keyString = sb.ToString();
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public QueryCacheStats GetStats()
    {
        lock (_lockObject)
        {
            return new QueryCacheStats
            {
                TotalEntries = _cache.Count,
                MaxEntries = _maxEntries,
                ExpiredEntries = _cache.Values.Count(v => v.ExpiresAt < DateTime.UtcNow)
            };
        }
    }
}

/// <summary>
/// Query cache statistics
/// </summary>
public class QueryCacheStats
{
    public int TotalEntries { get; set; }
    public int MaxEntries { get; set; }
    public int ExpiredEntries { get; set; }
}

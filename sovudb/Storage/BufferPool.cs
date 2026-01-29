using System.Collections.Concurrent;

namespace ovudb.Storage;

/// <summary>
/// Buffer pool for caching data pages in memory
/// Uses LRU (Least Recently Used) algorithm for page eviction
/// </summary>
public class BufferPool : IDisposable
{
    private readonly ConcurrentDictionary<string, Page> _pages = new();
    private readonly int _maxPages;
    private readonly int _pageSize;
    private readonly object _lockObject = new();
    private bool _disposed = false;

    // Statistics (use separate fields for thread safety)
    private long _totalAccesses = 0;
    private long _cacheHits = 0;
    private long _cacheMisses = 0;

    /// <summary>
    /// Buffer pool statistics
    /// </summary>
    public class BufferPoolStats
    {
        public int TotalPages { get; set; }
        public int DirtyPages { get; set; }
        public long TotalAccesses { get; set; }
        public long CacheHits { get; set; }
        public long CacheMisses { get; set; }
        public double HitRatio => TotalAccesses > 0 ? (double)CacheHits / TotalAccesses : 0;
    }

    public BufferPool(int maxPages = 1000, int pageSize = 8192)
    {
        _maxPages = maxPages;
        _pageSize = pageSize;
    }

    /// <summary>
    /// Get page from cache
    /// </summary>
    public Page? GetPage(int tableId, int pageNumber)
    {
        if (_disposed) return null;

        var key = GetPageKey(tableId, pageNumber);
        
        if (_pages.TryGetValue(key, out var page))
        {
            page.Touch();
            Interlocked.Increment(ref _cacheHits);
            Interlocked.Increment(ref _totalAccesses);
            return page;
        }

        Interlocked.Increment(ref _cacheMisses);
        Interlocked.Increment(ref _totalAccesses);
        return null;
    }

    /// <summary>
    /// Add page to cache
    /// </summary>
    public void PutPage(int tableId, int pageNumber, byte[] data)
    {
        if (_disposed) return;

        var key = GetPageKey(tableId, pageNumber);
        var page = new Page(tableId, pageNumber, data);

        lock (_lockObject)
        {
            // If limit reached, evict least recently used page
            if (_pages.Count >= _maxPages && !_pages.ContainsKey(key))
            {
                EvictLeastRecentlyUsed();
            }

            _pages[key] = page;
        }
    }

    /// <summary>
    /// Mark page as dirty
    /// </summary>
    public void MarkPageDirty(int tableId, int pageNumber)
    {
        if (_disposed) return;

        var key = GetPageKey(tableId, pageNumber);
        if (_pages.TryGetValue(key, out var page))
        {
            page.MarkDirty();
        }
    }

    /// <summary>
    /// Evict least recently used page
    /// </summary>
    private void EvictLeastRecentlyUsed()
    {
        Page? lruPage = null;
        string? lruKey = null;
        var oldestTime = DateTime.MaxValue;

        foreach (var kvp in _pages)
        {
            if (kvp.Value.LastAccessed < oldestTime && !kvp.Value.IsDirty)
            {
                oldestTime = kvp.Value.LastAccessed;
                lruPage = kvp.Value;
                lruKey = kvp.Key;
            }
        }

        // If all pages dirty, evict oldest
        if (lruKey == null)
        {
            foreach (var kvp in _pages)
            {
                if (kvp.Value.LastAccessed < oldestTime)
                {
                    oldestTime = kvp.Value.LastAccessed;
                    lruPage = kvp.Value;
                    lruKey = kvp.Key;
                }
            }
        }

        if (lruKey != null && _pages.TryRemove(lruKey, out var removedPage))
        {
            // If page is dirty, save to disk before eviction
            if (removedPage.IsDirty)
            {
                // Can add disk save logic here
                // For now just evict
            }
        }
    }

    /// <summary>
    /// Get all dirty pages for table
    /// </summary>
    public List<Page> GetDirtyPages(int tableId)
    {
        if (_disposed) return new List<Page>();

        return _pages.Values
            .Where(p => p.TableId == tableId && p.IsDirty)
            .ToList();
    }

    /// <summary>
    /// Clear all table pages from cache
    /// </summary>
    public void InvalidateTable(int tableId)
    {
        if (_disposed) return;

        var keysToRemove = _pages
            .Where(kvp => kvp.Value.TableId == tableId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _pages.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Clear all pages from cache
    /// </summary>
    public void Clear()
    {
        if (_disposed) return;

        lock (_lockObject)
        {
            _pages.Clear();
        }
    }

    /// <summary>
    /// Get buffer pool statistics
    /// </summary>
    public BufferPoolStats GetStats()
    {
        lock (_lockObject)
        {
            var totalAccesses = Interlocked.Read(ref _totalAccesses);
            var cacheHits = Interlocked.Read(ref _cacheHits);
            var cacheMisses = Interlocked.Read(ref _cacheMisses);
            
            return new BufferPoolStats
            {
                TotalPages = _pages.Count,
                DirtyPages = _pages.Values.Count(p => p.IsDirty),
                TotalAccesses = totalAccesses,
                CacheHits = cacheHits,
                CacheMisses = cacheMisses
            };
        }
    }

    /// <summary>
    /// Generate key for page
    /// </summary>
    private static string GetPageKey(int tableId, int pageNumber)
    {
        return $"{tableId}_{pageNumber}";
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Save all dirty pages before closing
        FlushDirtyPages();

        _disposed = true;
    }

    /// <summary>
    /// Flush all dirty pages to disk
    /// </summary>
    public void FlushDirtyPages()
    {
        // Implementation to be added when integrating with BinaryStorage
    }

    /// <summary>
    /// Page size
    /// </summary>
    public int PageSize => _pageSize;
}

using ovudb.Storage;
using Xunit;

namespace ovudb.Tests.Storage;

public class QueryCacheTests : IDisposable
{
    private readonly QueryCache _queryCache;

    public QueryCacheTests()
    {
        _queryCache = new QueryCache(maxEntries: 10, defaultTtl: TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Get_NonExistent_ReturnsDefault()
    {
        var result = _queryCache.Get<List<int>>("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void Put_AndGet_ReturnsSameData()
    {
        var data = new List<int> { 1, 2, 3, 4, 5 };
        var key = QueryCache.GenerateKey("test_table", "SELECT * FROM test_table");

        _queryCache.Put(key, data);
        var retrieved = _queryCache.Get<List<int>>(key);

        Assert.NotNull(retrieved);
        Assert.Equal(data, retrieved);
    }

    [Fact]
    public void Get_ExpiredEntry_ReturnsDefault()
    {
        var cache = new QueryCache(maxEntries: 10, defaultTtl: TimeSpan.FromMilliseconds(50));
        var data = new List<int> { 1, 2, 3 };
        var key = QueryCache.GenerateKey("test_table", "SELECT * FROM test_table");

        cache.Put(key, data);
        
        // Wait for expiration
        Thread.Sleep(100);

        var retrieved = cache.Get<List<int>>(key);
        Assert.Null(retrieved);
    }

    [Fact]
    public void Remove_RemovesEntry()
    {
        var data = new List<int> { 1, 2, 3 };
        var key = QueryCache.GenerateKey("test_table", "SELECT * FROM test_table");

        _queryCache.Put(key, data);
        _queryCache.Remove(key);

        var retrieved = _queryCache.Get<List<int>>(key);
        Assert.Null(retrieved);
    }

    [Fact]
    public void InvalidateTable_RemovesAllTableEntries()
    {
        var key1 = QueryCache.GenerateKey("table1", "SELECT * FROM table1");
        var key2 = QueryCache.GenerateKey("table2", "SELECT * FROM table2");
        var key3 = QueryCache.GenerateKey("table1", "SELECT * FROM table1 WHERE id=1");

        _queryCache.Put(key1, new List<int> { 1 }, "table1");
        _queryCache.Put(key2, new List<int> { 2 }, "table2");
        _queryCache.Put(key3, new List<int> { 3 }, "table1");

        _queryCache.InvalidateTable("table1");

        Assert.Null(_queryCache.Get<List<int>>(key1));
        Assert.Null(_queryCache.Get<List<int>>(key3));
        Assert.NotNull(_queryCache.Get<List<int>>(key2)); // Other table entries should remain
    }

    [Fact]
    public void Put_ExceedsMaxEntries_EvictsOldEntries()
    {
        var cache = new QueryCache(maxEntries: 5);

        // Fill cache to max
        for (int i = 0; i < 5; i++)
        {
            var key = QueryCache.GenerateKey("test_table", $"SELECT * FROM test_table WHERE id={i}");
            cache.Put(key, new List<int> { i });
        }

        // Add one more entry - should evict old
        var newKey = QueryCache.GenerateKey("test_table", "SELECT * FROM test_table WHERE id=99");
        cache.Put(newKey, new List<int> { 99 });

        var stats = cache.GetStats();
        Assert.True(stats.TotalEntries <= 5);
    }

    [Fact]
    public void GenerateKey_SameQuery_ReturnsSameKey()
    {
        var key1 = QueryCache.GenerateKey("table1", "SELECT * FROM table1 WHERE id=1");
        var key2 = QueryCache.GenerateKey("table1", "SELECT * FROM table1 WHERE id=1");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GenerateKey_DifferentQueries_ReturnsDifferentKeys()
    {
        var key1 = QueryCache.GenerateKey("table1", "SELECT * FROM table1 WHERE id=1");
        var key2 = QueryCache.GenerateKey("table1", "SELECT * FROM table1 WHERE id=2");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GenerateKey_WithParameters_ReturnsConsistentKey()
    {
        var params1 = new Dictionary<string, object> { ["id"] = 1, ["name"] = "test" };
        var params2 = new Dictionary<string, object> { ["name"] = "test", ["id"] = 1 }; // Different order

        var key1 = QueryCache.GenerateKey("table1", "SELECT * FROM table1", params1);
        var key2 = QueryCache.GenerateKey("table1", "SELECT * FROM table1", params2);

        // Keys should be same regardless of parameter order
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GetStats_ReturnsCorrectStatistics()
    {
        var key1 = QueryCache.GenerateKey("table1", "SELECT * FROM table1");
        var key2 = QueryCache.GenerateKey("table2", "SELECT * FROM table2");

        _queryCache.Put(key1, new List<int> { 1 });
        _queryCache.Put(key2, new List<int> { 2 });

        var stats = _queryCache.GetStats();
        Assert.Equal(2, stats.TotalEntries);
        Assert.Equal(10, stats.MaxEntries);
        Assert.Equal(0, stats.ExpiredEntries);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var key1 = QueryCache.GenerateKey("table1", "SELECT * FROM table1");
        var key2 = QueryCache.GenerateKey("table2", "SELECT * FROM table2");

        _queryCache.Put(key1, new List<int> { 1 });
        _queryCache.Put(key2, new List<int> { 2 });

        _queryCache.Clear();

        Assert.Null(_queryCache.Get<List<int>>(key1));
        Assert.Null(_queryCache.Get<List<int>>(key2));
        
        var stats = _queryCache.GetStats();
        Assert.Equal(0, stats.TotalEntries);
    }

    public void Dispose()
    {
        _queryCache.Clear();
    }
}

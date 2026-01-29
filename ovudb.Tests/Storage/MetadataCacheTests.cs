using ovudb.Storage;
using Xunit;

namespace ovudb.Tests.Storage;

public class MetadataCacheTests
{
    [Fact]
    public void GetSchema_NonExistent_ReturnsNull()
    {
        var cache = new MetadataCache();
        var schema = cache.GetSchema("nonexistent");
        Assert.Null(schema);
    }

    [Fact]
    public void PutSchema_AndGetSchema_ReturnsSameSchema()
    {
        var cache = new MetadataCache();
        var schema = new Dictionary<string, object>
        {
            ["Columns"] = new List<object>(),
            ["Indexes"] = new List<object>()
        };

        cache.PutSchema("test_table", schema);
        var retrieved = cache.GetSchema("test_table");

        Assert.NotNull(retrieved);
        Assert.Equal(schema, retrieved);
    }

    [Fact]
    public void GetMetadata_NonExistent_ReturnsNull()
    {
        var cache = new MetadataCache();
        var metadata = cache.GetMetadata("nonexistent");
        Assert.Null(metadata);
    }

    [Fact]
    public void PutMetadata_AndGetMetadata_ReturnsSameMetadata()
    {
        var cache = new MetadataCache();
        var metadata = new Dictionary<string, object>
        {
            ["table_id"] = 1,
            ["row_count"] = 100,
            ["last_modified"] = DateTime.UtcNow
        };

        cache.PutMetadata("test_table", metadata);
        var retrieved = cache.GetMetadata("test_table");

        Assert.NotNull(retrieved);
        Assert.Equal(metadata, retrieved);
    }

    [Fact]
    public void Invalidate_RemovesTableFromCache()
    {
        var cache = new MetadataCache();
        var schema = new Dictionary<string, object> { ["Columns"] = new List<object>() };
        var metadata = new Dictionary<string, object> { ["table_id"] = 1 };

        cache.PutSchema("test_table", schema);
        cache.PutMetadata("test_table", metadata);

        cache.Invalidate("test_table");

        Assert.Null(cache.GetSchema("test_table"));
        Assert.Null(cache.GetMetadata("test_table"));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new MetadataCache();
        cache.PutSchema("table1", new Dictionary<string, object>());
        cache.PutSchema("table2", new Dictionary<string, object>());

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Null(cache.GetSchema("table1"));
        Assert.Null(cache.GetSchema("table2"));
    }

    [Fact]
    public void Count_ReturnsNumberOfCachedTables()
    {
        var cache = new MetadataCache();
        Assert.Equal(0, cache.Count);

        cache.PutSchema("table1", new Dictionary<string, object>());
        Assert.Equal(1, cache.Count);

        cache.PutSchema("table2", new Dictionary<string, object>());
        Assert.Equal(2, cache.Count);

        cache.Invalidate("table1");
        Assert.Equal(1, cache.Count);
    }
}

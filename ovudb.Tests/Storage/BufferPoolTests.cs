using ovudb.Storage;
using Xunit;

namespace ovudb.Tests.Storage;

public class BufferPoolTests : IDisposable
{
    private readonly BufferPool _bufferPool;

    public BufferPoolTests()
    {
        _bufferPool = new BufferPool(maxPages: 10, pageSize: 8192);
    }

    [Fact]
    public void GetPage_NonExistent_ReturnsNull()
    {
        var page = _bufferPool.GetPage(1, 0);
        Assert.Null(page);
    }

    [Fact]
    public void PutPage_AndGetPage_ReturnsSamePage()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        _bufferPool.PutPage(1, 0, data);

        var page = _bufferPool.GetPage(1, 0);
        Assert.NotNull(page);
        Assert.Equal(1, page!.TableId);
        Assert.Equal(0, page.PageNumber);
        Assert.Equal(data, page.Data);
        Assert.False(page.IsDirty);
    }

    [Fact]
    public void GetPage_UpdatesAccessTime()
    {
        var data = new byte[] { 1, 2, 3 };
        _bufferPool.PutPage(1, 0, data);

        var page1 = _bufferPool.GetPage(1, 0);
        Assert.NotNull(page1);
        var firstAccess = page1!.LastAccessed;
        var firstCount = page1.AccessCount;

        Thread.Sleep(10);
        var page2 = _bufferPool.GetPage(1, 0);
        Assert.NotNull(page2);
        var secondAccess = page2!.LastAccessed;
        var secondCount = page2.AccessCount;

        Assert.True(secondAccess > firstAccess);
        Assert.True(secondCount > firstCount);
    }

    [Fact]
    public void MarkPageDirty_MarksPageAsDirty()
    {
        var data = new byte[] { 1, 2, 3 };
        _bufferPool.PutPage(1, 0, data);

        var page1 = _bufferPool.GetPage(1, 0);
        Assert.NotNull(page1);
        Assert.False(page1!.IsDirty);

        _bufferPool.MarkPageDirty(1, 0);

        var page2 = _bufferPool.GetPage(1, 0);
        Assert.NotNull(page2);
        Assert.True(page2!.IsDirty);
    }

    [Fact]
    public void PutPage_ExceedsMaxPages_EvictsOldPages()
    {
        // Fill pool to max
        for (int i = 0; i < 10; i++)
        {
            var data = new byte[] { (byte)i };
            _bufferPool.PutPage(1, i, data);
        }

        // Add one more page - should evict old
        var newData = new byte[] { 99 };
        _bufferPool.PutPage(1, 10, newData);

        // Verify new page exists
        var newPage = _bufferPool.GetPage(1, 10);
        Assert.NotNull(newPage);
        Assert.Equal(newData, newPage!.Data);

        // Stats should show max pages
        var stats = _bufferPool.GetStats();
        Assert.True(stats.TotalPages <= 10);
    }

    [Fact]
    public void InvalidateTable_RemovesAllPagesForTable()
    {
        _bufferPool.PutPage(1, 0, new byte[] { 1 });
        _bufferPool.PutPage(1, 1, new byte[] { 2 });
        _bufferPool.PutPage(2, 0, new byte[] { 3 });

        _bufferPool.InvalidateTable(1);

        Assert.Null(_bufferPool.GetPage(1, 0));
        Assert.Null(_bufferPool.GetPage(1, 1));
        Assert.NotNull(_bufferPool.GetPage(2, 0)); // Other table pages should remain
    }

    [Fact]
    public void GetDirtyPages_ReturnsOnlyDirtyPages()
    {
        _bufferPool.PutPage(1, 0, new byte[] { 1 });
        _bufferPool.PutPage(1, 1, new byte[] { 2 });
        _bufferPool.PutPage(1, 2, new byte[] { 3 });

        _bufferPool.MarkPageDirty(1, 0);
        _bufferPool.MarkPageDirty(1, 2);

        var dirtyPages = _bufferPool.GetDirtyPages(1);
        Assert.Equal(2, dirtyPages.Count);
        Assert.All(dirtyPages, p => Assert.True(p.IsDirty));
        Assert.Contains(dirtyPages, p => p.PageNumber == 0);
        Assert.Contains(dirtyPages, p => p.PageNumber == 2);
    }

    [Fact]
    public void GetStats_ReturnsCorrectStatistics()
    {
        _bufferPool.PutPage(1, 0, new byte[] { 1 });
        _bufferPool.PutPage(1, 1, new byte[] { 2 });
        _bufferPool.MarkPageDirty(1, 0);

        _bufferPool.GetPage(1, 0);
        _bufferPool.GetPage(1, 1);
        _bufferPool.GetPage(1, 0); // Repeat access

        var stats = _bufferPool.GetStats();
        Assert.Equal(2, stats.TotalPages);
        Assert.Equal(1, stats.DirtyPages);
        Assert.True(stats.CacheHits >= 2);
        Assert.True(stats.TotalAccesses >= 2);
        Assert.True(stats.HitRatio > 0);
    }

    [Fact]
    public void Clear_RemovesAllPages()
    {
        _bufferPool.PutPage(1, 0, new byte[] { 1 });
        _bufferPool.PutPage(1, 1, new byte[] { 2 });

        _bufferPool.Clear();

        Assert.Null(_bufferPool.GetPage(1, 0));
        Assert.Null(_bufferPool.GetPage(1, 1));
        
        var stats = _bufferPool.GetStats();
        Assert.Equal(0, stats.TotalPages);
    }

    public void Dispose()
    {
        _bufferPool.Dispose();
    }
}

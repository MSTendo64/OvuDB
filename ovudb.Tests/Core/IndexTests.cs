using ovudb.Core;
using Xunit;
using CoreIndex = ovudb.Core.Index;

namespace ovudb.Tests.Core;

public class IndexTests
{
    [Fact]
    public void Constructor_SetsNameAndColumnNames()
    {
        var index = new CoreIndex("idx_name", "Name", "Email");
        
        Assert.Equal("idx_name", index.Name);
        Assert.Equal(2, index.ColumnNames.Count);
        Assert.Contains("Name", index.ColumnNames);
        Assert.Contains("Email", index.ColumnNames);
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Unique_SetsIsUnique()
    {
        var index = new CoreIndex("idx_unique", "Email").Unique();
        
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Constructor_WithMultipleColumns()
    {
        var index = new CoreIndex("idx_composite", "FirstName", "LastName", "Email");
        
        Assert.Equal(3, index.ColumnNames.Count);
    }
}

using ovudb.Core;
using Xunit;

namespace ovudb.Tests.Core;

public class ColumnTests
{
    [Fact]
    public void Constructor_SetsNameAndDataType()
    {
        var column = new Column("TestColumn", DataType.String);
        
        Assert.Equal("TestColumn", column.Name);
        Assert.Equal(DataType.String, column.DataType);
        Assert.True(column.IsNullable);
        Assert.False(column.IsPrimaryKey);
        Assert.False(column.IsAutoIncrement);
    }

    [Fact]
    public void PrimaryKey_SetsPrimaryKeyAndNotNullable()
    {
        var column = new Column("Id", DataType.Integer).PrimaryKey();
        
        Assert.True(column.IsPrimaryKey);
        Assert.False(column.IsNullable);
    }

    [Fact]
    public void NotNull_SetsNullableToFalse()
    {
        var column = new Column("Name", DataType.String).NotNull();
        
        Assert.False(column.IsNullable);
    }

    [Fact]
    public void AutoIncrement_SetsAutoIncrement()
    {
        var column = new Column("Id", DataType.Integer).AutoIncrement();
        
        Assert.True(column.IsAutoIncrement);
    }

    [Fact]
    public void Unique_SetsUnique()
    {
        var column = new Column("Email", DataType.String).Unique();
        
        Assert.True(column.IsUnique);
    }

    [Fact]
    public void WithDefault_SetsDefaultValue()
    {
        var column = new Column("Status", DataType.String).WithDefault("Active");
        
        Assert.Equal("Active", column.DefaultValue);
    }

    [Fact]
    public void WithMaxLength_SetsMaxLength()
    {
        var column = new Column("Name", DataType.String).WithMaxLength(100);
        
        Assert.Equal(100, column.MaxLength);
    }

    [Fact]
    public void FluentInterface_AllowsChaining()
    {
        var column = new Column("Id", DataType.Integer)
            .PrimaryKey()
            .AutoIncrement()
            .NotNull();
        
        Assert.True(column.IsPrimaryKey);
        Assert.True(column.IsAutoIncrement);
        Assert.False(column.IsNullable);
    }
}

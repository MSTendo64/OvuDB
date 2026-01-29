using ovudb.Core;
using Xunit;

namespace ovudb.Tests.Core;

public class DataTypeTests
{
    [Fact]
    public void ToClrType_Integer_ReturnsIntType()
    {
        var clrType = DataType.Integer.ToClrType();
        Assert.Equal(typeof(int), clrType);
    }

    [Fact]
    public void ToClrType_Long_ReturnsLongType()
    {
        var clrType = DataType.Long.ToClrType();
        Assert.Equal(typeof(long), clrType);
    }

    [Fact]
    public void ToClrType_Double_ReturnsDoubleType()
    {
        var clrType = DataType.Double.ToClrType();
        Assert.Equal(typeof(double), clrType);
    }

    [Fact]
    public void ToClrType_String_ReturnsStringType()
    {
        var clrType = DataType.String.ToClrType();
        Assert.Equal(typeof(string), clrType);
    }

    [Fact]
    public void ToClrType_Boolean_ReturnsBoolType()
    {
        var clrType = DataType.Boolean.ToClrType();
        Assert.Equal(typeof(bool), clrType);
    }

    [Fact]
    public void ToClrType_DateTime_ReturnsDateTimeType()
    {
        var clrType = DataType.DateTime.ToClrType();
        Assert.Equal(typeof(DateTime), clrType);
    }

    [Fact]
    public void FromClrType_Int_ReturnsInteger()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(int));
        Assert.Equal(DataType.Integer, dataType);
    }

    [Fact]
    public void FromClrType_Long_ReturnsLong()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(long));
        Assert.Equal(DataType.Long, dataType);
    }

    [Fact]
    public void FromClrType_Double_ReturnsDouble()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(double));
        Assert.Equal(DataType.Double, dataType);
    }

    [Fact]
    public void FromClrType_Float_ReturnsDouble()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(float));
        Assert.Equal(DataType.Double, dataType);
    }

    [Fact]
    public void FromClrType_Decimal_ReturnsDouble()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(decimal));
        Assert.Equal(DataType.Double, dataType);
    }

    [Fact]
    public void FromClrType_String_ReturnsString()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(string));
        Assert.Equal(DataType.String, dataType);
    }

    [Fact]
    public void FromClrType_Bool_ReturnsBoolean()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(bool));
        Assert.Equal(DataType.Boolean, dataType);
    }

    [Fact]
    public void FromClrType_DateTime_ReturnsDateTime()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(DateTime));
        Assert.Equal(DataType.DateTime, dataType);
    }

    [Fact]
    public void FromClrType_UnknownType_ReturnsString()
    {
        var dataType = DataTypeExtensions.FromClrType(typeof(object));
        Assert.Equal(DataType.String, dataType);
    }
}

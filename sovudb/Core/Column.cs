namespace ovudb.Core;

/// <summary>
/// Represents table column
/// </summary>
public class Column
{
    public string Name { get; set; }
    public DataType DataType { get; set; }
    public int? MaxLength { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsNullable { get; set; }
    public bool IsAutoIncrement { get; set; }
    public object? DefaultValue { get; set; }
    public bool IsUnique { get; set; }

    public Column(string name, DataType dataType)
    {
        Name = name;
        DataType = dataType;
        IsNullable = true;
        IsPrimaryKey = false;
        IsAutoIncrement = false;
        IsUnique = false;
    }

    public Column PrimaryKey()
    {
        IsPrimaryKey = true;
        IsNullable = false;
        return this;
    }

    public Column NotNull()
    {
        IsNullable = false;
        return this;
    }

    public Column AutoIncrement()
    {
        IsAutoIncrement = true;
        return this;
    }

    public Column Unique()
    {
        IsUnique = true;
        return this;
    }

    public Column WithDefault(object? defaultValue)
    {
        DefaultValue = defaultValue;
        return this;
    }

    public Column WithMaxLength(int maxLength)
    {
        MaxLength = maxLength;
        return this;
    }
}

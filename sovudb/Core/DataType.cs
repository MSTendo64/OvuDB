namespace ovudb.Core;

/// <summary>
/// Supported data types in database
/// </summary>
public enum DataType
{
    Integer,
    Long,
    Double,
    String,
    Boolean,
    DateTime,
    Binary
}

/// <summary>
/// Extensions for working with data types
/// </summary>
public static class DataTypeExtensions
{
    public static Type ToClrType(this DataType dataType)
    {
        return dataType switch
        {
            DataType.Integer => typeof(int),
            DataType.Long => typeof(long),
            DataType.Double => typeof(double),
            DataType.String => typeof(string),
            DataType.Boolean => typeof(bool),
            DataType.DateTime => typeof(DateTime),
            DataType.Binary => typeof(byte[]),
            _ => typeof(object)
        };
    }

    public static DataType FromClrType(Type type)
    {
        if (type == typeof(int)) return DataType.Integer;
        if (type == typeof(long)) return DataType.Long;
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return DataType.Double;
        if (type == typeof(string)) return DataType.String;
        if (type == typeof(bool)) return DataType.Boolean;
        if (type == typeof(DateTime)) return DataType.DateTime;
        if (type == typeof(byte[])) return DataType.Binary;
        
        return DataType.String; // Default
    }
}

namespace ovudb.Storage;

/// <summary>
/// Models for database dumps
/// </summary>
public class DatabaseMetadata
{
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public Dictionary<string, TableMetadata> Tables { get; set; } = new();
}

public class TableMetadata
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public int RowCount { get; set; }
}

public class TableDump
{
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public Dictionary<string, object> Schema { get; set; } = new();
    public List<Dictionary<string, object>> Data { get; set; } = new();
    public TableMetadata? Metadata { get; set; }
    public int RowCount { get; set; }
}

public class FullDatabaseDump
{
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public DatabaseMetadata DatabaseMetadata { get; set; } = new();
    public Dictionary<string, TableDump> Tables { get; set; } = new();
}

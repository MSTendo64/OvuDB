namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table tables_priv - table access rights
/// </summary>
public class SystemTablesPriv
{
    public int Id { get; set; }
    public string Host { get; set; } = "%";
    public string Db { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Grantor { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool TablePriv { get; set; } = false;
    public bool ColumnPriv { get; set; } = false;
}

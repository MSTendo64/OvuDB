namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table columns_priv - column access rights
/// </summary>
public class SystemColumnsPriv
{
    public int Id { get; set; }
    public string Host { get; set; } = "%";
    public string Db { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool ColumnPriv { get; set; } = false;
}

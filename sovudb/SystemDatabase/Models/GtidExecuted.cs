namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table gtid_executed - executed GTID transactions (for replication)
/// </summary>
public class SystemGtidExecuted
{
    public int Id { get; set; }
    public string SourceUuid { get; set; } = string.Empty;
    public long IntervalStart { get; set; }
    public long IntervalEnd { get; set; }
}

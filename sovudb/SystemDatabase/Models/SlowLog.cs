namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table slow_log - slow query log
/// </summary>
public class SystemSlowLog
{
    public int Id { get; set; }
    public DateTime StartTime { get; set; }
    public string UserHost { get; set; } = string.Empty;
    public long QueryTime { get; set; } // In milliseconds
    public long LockTime { get; set; } // In milliseconds
    public long RowsSent { get; set; }
    public long RowsExamined { get; set; }
    public string Db { get; set; } = string.Empty;
    public long LastInsertId { get; set; }
    public long InsertId { get; set; }
    public long ServerId { get; set; }
    public string SqlText { get; set; } = string.Empty;
    public long ThreadId { get; set; }
}

namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table general_log - general query log
/// </summary>
public class SystemGeneralLog
{
    public int Id { get; set; }
    public DateTime EventTime { get; set; }
    public string UserHost { get; set; } = string.Empty;
    public long ThreadId { get; set; }
    public long ServerId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string Argument { get; set; } = string.Empty;
}

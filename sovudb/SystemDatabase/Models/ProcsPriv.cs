namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table procs_priv - procedure access rights
/// </summary>
public class SystemProcsPriv
{
    public int Id { get; set; }
    public string Host { get; set; } = "%";
    public string Db { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string RoutineName { get; set; } = string.Empty;
    public string RoutineType { get; set; } = "PROCEDURE"; // PROCEDURE or FUNCTION
    public string Grantor { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool ProcPriv { get; set; } = false;
}

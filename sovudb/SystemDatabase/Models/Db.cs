namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table db - database access rights
/// </summary>
public class SystemDb
{
    public int Id { get; set; }
    public string Host { get; set; } = "%";
    public string Db { get; set; } = string.Empty; // Database name
    public string User { get; set; } = string.Empty; // User name
    public bool SelectPriv { get; set; } = false;
    public bool InsertPriv { get; set; } = false;
    public bool UpdatePriv { get; set; } = false;
    public bool DeletePriv { get; set; } = false;
    public bool CreatePriv { get; set; } = false;
    public bool DropPriv { get; set; } = false;
    public bool GrantPriv { get; set; } = false;
    public bool ReferencesPriv { get; set; } = false;
    public bool IndexPriv { get; set; } = false;
    public bool AlterPriv { get; set; } = false;
}

namespace ovudb.SystemDatabase.Models;

/// <summary>
/// System table user - user accounts
/// </summary>
public class SystemUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Host { get; set; } = "%"; // Host from which access is allowed (% = any)
    public bool SelectPriv { get; set; } = false;
    public bool InsertPriv { get; set; } = false;
    public bool UpdatePriv { get; set; } = false;
    public bool DeletePriv { get; set; } = false;
    public bool CreatePriv { get; set; } = false;
    public bool DropPriv { get; set; } = false;
    public bool ReloadPriv { get; set; } = false;
    public bool ShutdownPriv { get; set; } = false;
    public bool ProcessPriv { get; set; } = false;
    public bool FilePriv { get; set; } = false;
    public bool GrantPriv { get; set; } = false;
    public bool ReferencesPriv { get; set; } = false;
    public bool IndexPriv { get; set; } = false;
    public bool AlterPriv { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime LastLogin { get; set; }
}

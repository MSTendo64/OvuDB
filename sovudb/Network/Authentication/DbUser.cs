namespace ovudb.Network.Authentication;

/// <summary>
/// User model for database storage
/// </summary>
public class DbUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Databases { get; set; } = "*"; // JSON array as string
    public string Privileges { get; set; } = "SELECT,INSERT,UPDATE,DELETE"; // JSON array as string
    public DateTime CreatedAt { get; set; }
    public DateTime LastLogin { get; set; }
}

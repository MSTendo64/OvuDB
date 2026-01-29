namespace ovudb.Network.Authentication;

/// <summary>
/// Database user
/// </summary>
public class User
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public List<string> Databases { get; set; } = new();
    public List<string> Privileges { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime LastLogin { get; set; }
}

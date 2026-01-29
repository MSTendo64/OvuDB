namespace ovudb.Network;

/// <summary>
/// Request from client to server
/// </summary>
public class Request
{
    public string Command { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
    public string? Database { get; set; }
    public string? Table { get; set; }
}

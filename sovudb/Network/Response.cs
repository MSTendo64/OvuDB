namespace ovudb.Network;

/// <summary>
/// Server response to client
/// </summary>
public class Response
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
}

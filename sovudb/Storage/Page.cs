namespace ovudb.Storage;

/// <summary>
/// Represents a data page in the buffer pool
/// </summary>
public class Page
{
    public int TableId { get; set; }
    public int PageNumber { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public bool IsDirty { get; set; }
    public DateTime LastAccessed { get; set; }
    public int AccessCount { get; set; }

    public Page(int tableId, int pageNumber, byte[] data)
    {
        TableId = tableId;
        PageNumber = pageNumber;
        Data = data;
        LastAccessed = DateTime.UtcNow;
        AccessCount = 1;
        IsDirty = false;
    }

    /// <summary>
    /// Update access time
    /// </summary>
    public void Touch()
    {
        LastAccessed = DateTime.UtcNow;
        AccessCount++;
    }

    /// <summary>
    /// Mark page as dirty
    /// </summary>
    public void MarkDirty()
    {
        IsDirty = true;
    }
}

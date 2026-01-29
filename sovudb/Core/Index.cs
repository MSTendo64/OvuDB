namespace ovudb.Core;

/// <summary>
/// Represents table index
/// </summary>
public class Index
{
    public string Name { get; set; }
    public List<string> ColumnNames { get; set; }
    public bool IsUnique { get; set; }

    public Index(string name, params string[] columnNames)
    {
        Name = name;
        ColumnNames = new List<string>(columnNames);
        IsUnique = false;
    }

    public Index Unique()
    {
        IsUnique = true;
        return this;
    }
}

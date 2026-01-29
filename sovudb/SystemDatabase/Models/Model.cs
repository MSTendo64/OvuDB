namespace ovudb.SystemDatabase.Models;

/// <summary>
/// Model (table template) for system database
/// </summary>
public class Model
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ModelType { get; set; } = "perm"; // "perm" or "temp"
    public string FieldsJson { get; set; } = string.Empty; // JSON with model fields
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Model field
/// </summary>
public class ModelField
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsKey { get; set; }
    public bool IsNullable { get; set; } = true;
    public object? DefaultValue { get; set; }
}

using System.Collections.Generic;
using System.Text.Json;

namespace ovudb.Storage;

/// <summary>
/// File storage in PostgreSQL style
/// Structure:
/// - data/ - main data directory
///   - {tableName}.json - table data files
///   - _metadata.json - database metadata
/// </summary>
public class FileStorage : IStorage
{
    private readonly string _dataDirectory;
    private const string MetadataFileName = "_metadata.json";
    private const string DumpExtension = ".dump.json";

    public FileStorage(string dataDirectory = "data")
    {
        _dataDirectory = dataDirectory;
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }
        InitializeMetadata();
    }

    /// <summary>
    /// Initialize metadata file
    /// </summary>
    private void InitializeMetadata()
    {
        var metadataPath = GetMetadataPath();
        if (!File.Exists(metadataPath))
        {
            var metadata = new DatabaseMetadata
            {
                Version = "1.0",
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                Tables = new Dictionary<string, TableMetadata>()
            };
            SaveMetadata(metadata);
        }
    }

    private string GetTablePath(string tableName)
    {
        return Path.Combine(_dataDirectory, $"{SanitizeFileName(tableName)}.json");
    }

    private string GetMetadataPath()
    {
        return Path.Combine(_dataDirectory, MetadataFileName);
    }

    private string GetDumpPath(string tableName)
    {
        return Path.Combine(_dataDirectory, $"{SanitizeFileName(tableName)}{DumpExtension}");
    }

    /// <summary>
    /// Sanitize file name (remove invalid characters)
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Load database metadata
    /// </summary>
    private DatabaseMetadata LoadMetadata()
    {
        var path = GetMetadataPath();
        if (!File.Exists(path))
        {
            return new DatabaseMetadata
            {
                Version = "1.0",
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                Tables = new Dictionary<string, TableMetadata>()
            };
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DatabaseMetadata>(json) ?? new DatabaseMetadata();
    }

    /// <summary>
    /// Save database metadata
    /// </summary>
    private void SaveMetadata(DatabaseMetadata metadata)
    {
        metadata.LastModified = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(GetMetadataPath(), json);
    }

    /// <summary>
    /// Update table metadata
    /// </summary>
    private void UpdateTableMetadata(string tableName, Dictionary<string, object> schema)
    {
        var metadata = LoadMetadata();
        if (!metadata.Tables.ContainsKey(tableName))
        {
            metadata.Tables[tableName] = new Storage.TableMetadata
            {
                Name = tableName,
                CreatedAt = DateTime.UtcNow
            };
        }

        metadata.Tables[tableName].LastModified = DateTime.UtcNow;
        
        // Safe RowCount conversion handling JsonElement
        if (schema.ContainsKey("RowCount"))
        {
            var rowCountValue = schema["RowCount"];
            metadata.Tables[tableName].RowCount = ConvertRowCountToInt(rowCountValue);
        }
        else
        {
            metadata.Tables[tableName].RowCount = 0;
        }

        SaveMetadata(metadata);
    }

    /// <summary>
    /// Convert RowCount value to int (handles JsonElement)
    /// </summary>
    private int ConvertRowCountToInt(object? value)
    {
        if (value == null) return 0;

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is System.Text.Json.JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                return jsonElement.GetInt32();
            }
            return 0;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    public void SaveTable(string tableName, Dictionary<string, object> schema, List<Dictionary<string, object>> rows)
    {
        // Update row count in schema
        var schemaWithRowCount = new Dictionary<string, object>(schema)
        {
            ["RowCount"] = rows.Count,
            ["LastModified"] = DateTime.UtcNow.ToString("O")
        };

        var tableData = new TableData
        {
            Schema = schemaWithRowCount,
            Rows = rows,
            Metadata = new TableFileMetadata
            {
                TableName = tableName,
                Version = "1.0",
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                RowCount = rows.Count
            }
        };

        var json = JsonSerializer.Serialize(tableData, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        File.WriteAllText(GetTablePath(tableName), json);
        UpdateTableMetadata(tableName, schemaWithRowCount);
    }

    public (Dictionary<string, object> schema, List<Dictionary<string, object>> rows)? LoadTable(string tableName)
    {
        var path = GetTablePath(tableName);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        var tableData = JsonSerializer.Deserialize<TableData>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (tableData == null)
        {
            return null;
        }

        // Update metadata on load
        if (tableData.Metadata != null)
        {
            UpdateTableMetadata(tableName, tableData.Schema);
        }

        return (tableData.Schema, tableData.Rows);
    }

    public void DeleteTable(string tableName)
    {
        var path = GetTablePath(tableName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        // Remove from metadata
        var metadata = LoadMetadata();
        metadata.Tables.Remove(tableName);
        SaveMetadata(metadata);
    }

    public bool TableExists(string tableName)
    {
        return File.Exists(GetTablePath(tableName));
    }

    public List<string> GetTableNames()
    {
        var files = Directory.GetFiles(_dataDirectory, "*.json")
            .Where(f => !Path.GetFileName(f).StartsWith("_") && !f.EndsWith(DumpExtension))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
        return files;
    }

    public string CreateDump(string tableName)
    {
        var loaded = LoadTable(tableName);
        if (!loaded.HasValue)
        {
            throw new InvalidOperationException($"Table {tableName} not found");
        }

        var (schema, rows) = loaded.Value;
        var metadata = LoadMetadata();
        var tableMetadata = metadata.Tables.GetValueOrDefault(tableName);

        var dump = new Storage.TableDump
        {
            Version = "1.0",
            CreatedAt = DateTime.UtcNow,
            DatabaseName = Path.GetFileName(_dataDirectory),
            TableName = tableName,
            Schema = schema,
            Data = rows,
            Metadata = tableMetadata,
            RowCount = rows.Count
        };

        var json = JsonSerializer.Serialize(dump, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return json;
    }

    public void RestoreFromDump(string tableName, string dumpJson)
    {
        TableDump? dump;
        try
        {
            dump = JsonSerializer.Deserialize<TableDump>(dumpJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid dump format", ex);
        }

        if (dump == null)
        {
            throw new InvalidOperationException("Invalid dump format");
        }

        SaveTable(dump.TableName, dump.Schema, dump.Data);
    }

    public string CreateFullDump()
    {
        var metadata = LoadMetadata();
        var tables = GetTableNames();
        var fullDump = new Storage.FullDatabaseDump
        {
            Version = "1.0",
            CreatedAt = DateTime.UtcNow,
            DatabaseName = Path.GetFileName(_dataDirectory),
            DatabaseMetadata = metadata,
            Tables = new Dictionary<string, Storage.TableDump>()
        };

        foreach (var tableName in tables)
        {
            var loaded = LoadTable(tableName);
            if (loaded.HasValue)
            {
                var (schema, rows) = loaded.Value;
                fullDump.Tables[tableName] = new Storage.TableDump
                {
                    Version = "1.0",
                    CreatedAt = DateTime.UtcNow,
                    DatabaseName = fullDump.DatabaseName,
                    TableName = tableName,
                    Schema = schema,
                    Data = rows,
                    Metadata = metadata.Tables.GetValueOrDefault(tableName),
                    RowCount = rows.Count
                };
            }
        }

        var json = JsonSerializer.Serialize(fullDump, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return json;
    }

    public void RestoreFromFullDump(string fullDumpJson)
    {
        Storage.FullDatabaseDump? fullDump;
        try
        {
            fullDump = JsonSerializer.Deserialize<Storage.FullDatabaseDump>(fullDumpJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid full dump format", ex);
        }

        if (fullDump == null)
        {
            throw new InvalidOperationException("Invalid full dump format");
        }

        // Restore metadata
        SaveMetadata(fullDump.DatabaseMetadata);

        // Restore tables
        foreach (var tableDump in fullDump.Tables.Values)
        {
            SaveTable(tableDump.TableName, tableDump.Schema, tableDump.Data);
        }
    }

    /// <summary>
    /// Save dump to file
    /// </summary>
    public void SaveDumpToFile(string tableName, string dumpJson)
    {
        var dumpPath = GetDumpPath(tableName);
        File.WriteAllText(dumpPath, dumpJson);
    }

    /// <summary>
    /// Load dump from file
    /// </summary>
    public string LoadDumpFromFile(string tableName)
    {
        var dumpPath = GetDumpPath(tableName);
        if (!File.Exists(dumpPath))
        {
            throw new FileNotFoundException($"Dump file {dumpPath} not found");
        }
        return File.ReadAllText(dumpPath);
    }

    // Helper classes for serialization

    private class TableData
    {
        public Dictionary<string, object> Schema { get; set; } = new();
        public List<Dictionary<string, object>> Rows { get; set; } = new();
        public TableFileMetadata? Metadata { get; set; }
    }

    private class TableFileMetadata
    {
        public string TableName { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0";
        public DateTime CreatedAt { get; set; }
        public DateTime LastModified { get; set; }
        public int RowCount { get; set; }
    }

}

using ovudb.Storage;

namespace ovudb.Core;

/// <summary>
/// Main database class
/// </summary>
public class Database
{
    private readonly IStorage _storage;
    private readonly Dictionary<string, object> _tables = new();
    private readonly string _name;

    public Database(string name, IStorage? storage = null, string? dataDirectory = null)
    {
        _name = name;
        
        if (storage != null)
        {
            _storage = storage;
        }
        else
        {
            // Determine data directory
            var directory = dataDirectory ?? "data";
            
            // Database ID is computed as hash of name
            // For system database use fixed ID
            int databaseId;
            if (name.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
            {
                // Fixed ID for system database
                databaseId = 1; // Always use ID = 1 for ovusys
            }
            else
            {
                // For regular databases compute ID as hash of name
                databaseId = Math.Abs(name.GetHashCode()) % 1000000 + 1000; // Start from 1000
            }
            
            _storage = new BinaryStorage(directory, databaseId);
        }
    }

    /// <summary>
    /// Get or create table
    /// </summary>
    public Table<T> GetTable<T>(string tableName) where T : class, new()
    {
        var key = $"{tableName}_{typeof(T).Name}";
        
        if (_tables.TryGetValue(key, out var table))
        {
            return (Table<T>)table;
        }

        var newTable = new Table<T>(tableName, _storage);
        _tables[key] = newTable;
        return newTable;
    }

    /// <summary>
    /// Create table if not exists
    /// </summary>
    public Table<T> CreateTable<T>(string tableName) where T : class, new()
    {
        // Check if table with this name exists (regardless of type)
        if (TableExists(tableName))
        {
            throw new InvalidOperationException($"Table {tableName} already exists in database {_name}");
        }
        
        // Check if table with this name is being created for another type
        var existingTableKey = _tables.Keys.FirstOrDefault(k => k.StartsWith($"{tableName}_"));
        if (existingTableKey != null)
        {
            throw new InvalidOperationException($"Table {tableName} already exists in database {_name} (created for another type)");
        }
        
        var table = GetTable<T>(tableName);
        table.CreateIfNotExists();
        return table;
    }

    /// <summary>
    /// Drop table
    /// </summary>
    public void DropTable(string tableName)
    {
        _storage.DeleteTable(tableName);
        var keysToRemove = _tables.Keys.Where(k => k.StartsWith($"{tableName}_")).ToList();
        foreach (var key in keysToRemove)
        {
            _tables.Remove(key);
        }
    }

    /// <summary>
    /// Check if table exists
    /// </summary>
    public bool TableExists(string tableName)
    {
        return _storage.TableExists(tableName);
    }

    /// <summary>
    /// Get list of all tables
    /// </summary>
    public List<string> GetTableNames()
    {
        return _storage.GetTableNames();
    }

    /// <summary>
    /// Database name
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Get data storage
    /// </summary>
    internal IStorage GetStorage() => _storage;

    /// <summary>
    /// Create table dump in JSON format
    /// </summary>
    public string CreateTableDump(string tableName)
    {
        return _storage.CreateDump(tableName);
    }

    /// <summary>
    /// Restore table from dump
    /// </summary>
    public void RestoreTableFromDump(string tableName, string dumpJson)
    {
        _storage.RestoreFromDump(tableName, dumpJson);
        // Update table cache
        var keysToRemove = _tables.Keys.Where(k => k.StartsWith($"{tableName}_")).ToList();
        foreach (var key in keysToRemove)
        {
            _tables.Remove(key);
        }
    }

    /// <summary>
    /// Create full database dump
    /// </summary>
    public string CreateFullDump()
    {
        return _storage.CreateFullDump();
    }

    /// <summary>
    /// Restore database from full dump
    /// </summary>
    public void RestoreFromFullDump(string fullDumpJson)
    {
        _storage.RestoreFromFullDump(fullDumpJson);
        // Clear table cache
        _tables.Clear();
    }

    /// <summary>
    /// Save table dump to file
    /// </summary>
    public void SaveTableDumpToFile(string tableName, string? filePath = null)
    {
        var dumpJson = _storage.CreateDump(tableName);
        if (string.IsNullOrEmpty(filePath))
        {
            _storage.SaveDumpToFile(tableName, dumpJson);
        }
        else
        {
            File.WriteAllText(filePath, dumpJson);
        }
    }

    /// <summary>
    /// Restore table from dump file
    /// </summary>
    public void RestoreTableFromDumpFile(string tableName, string? filePath = null)
    {
        string dumpJson;
        if (string.IsNullOrEmpty(filePath))
        {
            dumpJson = _storage.LoadDumpFromFile(tableName);
        }
        else
        {
            dumpJson = File.ReadAllText(filePath);
        }
        RestoreTableFromDump(tableName, dumpJson);
    }
}

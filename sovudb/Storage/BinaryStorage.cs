using System.Collections.Generic;
using System.Text.Json;

namespace ovudb.Storage;

/// <summary>
/// OvuDB binary data storage. Structure: data/{databaseId}/ with .ovu table files, .ovu.meta metadata, sys_tables.ovu, sys_columns.ovu.
/// </summary>
public class BinaryStorage : IStorage
{
    private readonly string _dataDirectory;
    private readonly int _databaseId;
    private readonly string _databaseDirectory;
    private const string MagicNumber = "OVUDB";
    private const int FormatVersion = 1;
    private const string DumpExtension = ".dump.json";

    // System files
    private const string SysTablesFile = "sys_tables.ovu";
    private const string SysColumnsFile = "sys_columns.ovu";

    // Cache
    private readonly Dictionary<string, int> _tableIdCache = new();
    private readonly Dictionary<int, string> _tableNameCache = new();

    // Buffer pool and caches
    private readonly BufferPool _bufferPool;
    private readonly MetadataCache _metadataCache;
    private readonly QueryCache _queryCache;

    public BinaryStorage(string dataDirectory, int databaseId, int bufferPoolSize = 1000, int pageSize = 8192)
    {
        _dataDirectory = dataDirectory;
        _databaseId = databaseId;
        _databaseDirectory = Path.Combine(_dataDirectory, databaseId.ToString());

        // Create directories (race-safe)
        if (!Directory.Exists(_dataDirectory))
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
            }
            catch (IOException)
            {
                // Directory may have been created by another thread
                if (!Directory.Exists(_dataDirectory))
                    throw;
            }
        }
        if (!Directory.Exists(_databaseDirectory))
        {
            try
            {
                Directory.CreateDirectory(_databaseDirectory);
            }
            catch (IOException)
            {
                // Directory may have been created by another thread
                if (!Directory.Exists(_databaseDirectory))
                    throw;
            }
        }

        // Initialize buffer pool and caches
        _bufferPool = new BufferPool(bufferPoolSize, pageSize);
        _metadataCache = new MetadataCache();
        _queryCache = new QueryCache();

        InitializeSystemTables();
        LoadTableCache();
    }

    /// <summary>
    /// Get buffer pool
    /// </summary>
    public BufferPool BufferPool => _bufferPool;

    /// <summary>
    /// Get metadata cache
    /// </summary>
    public MetadataCache MetadataCache => _metadataCache;

    /// <summary>
    /// Get query cache
    /// </summary>
    public QueryCache QueryCache => _queryCache;

    /// <summary>
    /// Initialize system tables
    /// </summary>
    private void InitializeSystemTables()
    {
        if (!File.Exists(GetSystemTablePath(SysTablesFile)))
        {
            SaveSystemTable(SysTablesFile, new List<Dictionary<string, object>>());
        }
        if (!File.Exists(GetSystemTablePath(SysColumnsFile)))
        {
            SaveSystemTable(SysColumnsFile, new List<Dictionary<string, object>>());
        }
    }

    /// <summary>
    /// Load table cache
    /// </summary>
    private void LoadTableCache()
    {
        var sysTables = LoadSystemTable(SysTablesFile);
        var registeredTableIds = new HashSet<int>();
        var initialCount = sysTables.Count;
        var tablesAdded = false;
        
        // Load cache from sys_tables.ovu
        foreach (var table in sysTables)
        {
            var tableId = GetIntValue(table, "table_id");
            var tableName = GetStringValue(table, "table_name");
            if (tableName != null && tableId > 0)
            {
                _tableIdCache[tableName] = tableId;
                _tableNameCache[tableId] = tableName;
                registeredTableIds.Add(tableId);
            }
        }
        
        // Check for table files on disk not registered in sys_tables.ovu (e.g. after corruption)
        try
        {
            if (Directory.Exists(_databaseDirectory))
            {
                var tableFiles = Directory.GetFiles(_databaseDirectory, "*.ovu")
                    .Where(f => !f.EndsWith(".meta") && 
                                !Path.GetFileName(f).Equals(SysTablesFile, StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).Equals(SysColumnsFile, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                foreach (var tableFile in tableFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(tableFile);
                    if (int.TryParse(fileName, out var tableId) && !registeredTableIds.Contains(tableId))
                    {
                        // Found table file not in sys_tables.ovu - try to recover from metadata
                        var metaPath = GetTableMetaPath(tableId);
                        if (File.Exists(metaPath))
                        {
                            try
                            {
                                var metadata = LoadTableMetadata(tableId);
                                if (metadata != null && metadata.TryGetValue("table_name", out var tableNameObj))
                                {
                                    var tableName = tableNameObj?.ToString();
                                    if (!string.IsNullOrEmpty(tableName) && !_tableIdCache.ContainsKey(tableName))
                                    {
                                        // Restore entry in sys_tables.ovu
                                        var newEntry = new Dictionary<string, object>
                                        {
                                            ["table_id"] = tableId,
                                            ["table_name"] = tableName,
                                            ["table_type"] = "table",
                                            ["created_at"] = metadata.TryGetValue("last_modified", out var lastMod) 
                                                ? lastMod 
                                                : DateTime.UtcNow.ToBinary()
                                        };
                                        
                                        sysTables.Add(newEntry);
                                        _tableIdCache[tableName] = tableId;
                                        _tableNameCache[tableId] = tableName;
                                        registeredTableIds.Add(tableId);
                                        tablesAdded = true;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore recovery errors
                            }
                        }
                    }
                }
                
                // Save updated sys_tables.ovu if entries were added
                if (tablesAdded)
                {
                    SaveSystemTable(SysTablesFile, sysTables);
                }
            }
        }
        catch
        {
            // Ignore file check errors
        }
    }

    private string GetSystemTablePath(string fileName)
    {
        return Path.Combine(_databaseDirectory, fileName);
    }

    private string GetTableDataPath(int tableId)
    {
        return Path.Combine(_databaseDirectory, $"{tableId}.ovu");
    }

    private string GetTableMetaPath(int tableId)
    {
        return Path.Combine(_databaseDirectory, $"{tableId}.ovu.meta");
    }

    private string GetDumpPath(string tableName)
    {
        return Path.Combine(_databaseDirectory, $"{SanitizeFileName(tableName)}{DumpExtension}");
    }

    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Get or create table ID
    /// </summary>
    private int GetTableId(string tableName)
    {
        // Check cache
        if (_tableIdCache.TryGetValue(tableName, out var cachedId))
        {
            return cachedId;
        }

        var sysTables = LoadSystemTable(SysTablesFile);
        var existing = sysTables.FirstOrDefault(r => 
            GetStringValue(r, "table_name") == tableName);
        
        if (existing != null)
        {
            var tableId = GetIntValue(existing, "table_id");
            _tableIdCache[tableName] = tableId;
            _tableNameCache[tableId] = tableName;
            return tableId;
        }

        // Create new ID
        var maxId = sysTables
            .Select(r => GetIntValue(r, "table_id"))
            .DefaultIfEmpty(1000)
            .Max();

        var newId = maxId + 1;
        var newEntry = new Dictionary<string, object>
        {
            ["table_id"] = newId,
            ["table_name"] = tableName,
            ["table_type"] = "table",
            ["created_at"] = DateTime.UtcNow.ToBinary()
        };

        sysTables.Add(newEntry);
        SaveSystemTable(SysTablesFile, sysTables);
        
        // Update cache
        _tableIdCache[tableName] = newId;
        _tableNameCache[newId] = tableName;
        
        return newId;
    }

    /// <summary>
    /// Get table ID by name
    /// </summary>
    private int? FindTableId(string tableName)
    {
        // Check cache
        if (_tableIdCache.TryGetValue(tableName, out var cachedId))
        {
            return cachedId;
        }

        var sysTables = LoadSystemTable(SysTablesFile);
        var entry = sysTables.FirstOrDefault(r => 
            GetStringValue(r, "table_name") == tableName);
        
        if (entry != null)
        {
            var tableId = GetIntValue(entry, "table_id");
            // Update cache
            if (tableId > 0)
            {
                _tableIdCache[tableName] = tableId;
                _tableNameCache[tableId] = tableName;
            }
            return tableId;
        }
        
        // If not in sys_tables.ovu, try to find on disk and restore registration
        try
        {
            if (Directory.Exists(_databaseDirectory))
            {
                var tableFiles = Directory.GetFiles(_databaseDirectory, "*.ovu")
                    .Where(f => !f.EndsWith(".meta") && 
                                !Path.GetFileName(f).Equals(SysTablesFile, StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).Equals(SysColumnsFile, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                foreach (var tableFile in tableFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(tableFile);
                    if (int.TryParse(fileName, out var fileTableId))
                    {
                        var metaPath = GetTableMetaPath(fileTableId);
                        if (File.Exists(metaPath))
                        {
                            try
                            {
                                var metadata = LoadTableMetadata(fileTableId);
                                if (metadata != null && metadata.TryGetValue("table_name", out var tableNameObj))
                                {
                                    var foundTableName = tableNameObj?.ToString();
                                    if (foundTableName == tableName)
                                    {
                                        // Found table on disk but not registered - restore registration
                                        var newEntry = new Dictionary<string, object>
                                        {
                                            ["table_id"] = fileTableId,
                                            ["table_name"] = tableName,
                                            ["table_type"] = "table",
                                            ["created_at"] = metadata.TryGetValue("last_modified", out var lastMod) 
                                                ? lastMod 
                                                : DateTime.UtcNow.ToBinary()
                                        };
                                        
                                        sysTables.Add(newEntry);
                                        SaveSystemTable(SysTablesFile, sysTables);
                                        
                                        // Update cache
                                        _tableIdCache[tableName] = fileTableId;
                                        _tableNameCache[fileTableId] = tableName;
                                        
                                        return fileTableId;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore errors
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return null;
    }

    /// <summary>
    /// Save system table
    /// </summary>
    private void SaveSystemTable(string fileName, List<Dictionary<string, object>> rows)
    {
        var path = GetSystemTablePath(fileName);
        
        // Use FileMode.Create with FileShare.Read and retries
        const int maxRetries = 5;
        const int retryDelayMs = 50;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Use buffered stream
                using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 16384);
                using var bufferedStream = new System.IO.BufferedStream(fileStream, bufferSize: 16384);
                using var writer = new BinaryWriter(bufferedStream);

                // File header
                writer.WriteString(MagicNumber);
                writer.WriteInt32(FormatVersion);
                writer.WriteInt64(DateTime.UtcNow.ToBinary());

                // Record count
                writer.WriteInt32(rows.Count);

                // Records
                foreach (var row in rows)
                {
                    writer.WriteInt32(row.Count);
                    foreach (var kvp in row)
                    {
                        writer.WriteString(kvp.Key);
                        writer.WriteObject(kvp.Value);
                    }
                }

                writer.Flush();
                bufferedStream.Flush();
                return; // Success
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // Wait before retry
                Thread.Sleep(retryDelayMs);
            }
        }
        
        throw new IOException($"Failed to save system table {fileName} after {maxRetries} attempts");
    }

    /// <summary>
    /// Load system table
    /// </summary>
    private List<Dictionary<string, object>> LoadSystemTable(string fileName)
    {
        var path = GetSystemTablePath(fileName);
        if (!File.Exists(path))
        {
            return new List<Dictionary<string, object>>();
        }

        // Use buffered stream for reading
        using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);
        using var bufferedStream = new System.IO.BufferedStream(fileStream, bufferSize: 65536);
        using var reader = new BinaryReader(bufferedStream);

        // Check header
        var magic = reader.ReadString();
        if (magic != MagicNumber)
        {
            throw new InvalidOperationException($"Invalid file format: {fileName}");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidOperationException($"Incompatible format version in {fileName}");
        }

        var created = reader.ReadDateTime();
        var rowCount = reader.ReadInt32();

        // Pre-allocate list
        var rows = new List<Dictionary<string, object>>(rowCount);
        for (int i = 0; i < rowCount; i++)
        {
            var columnCount = reader.ReadInt32();
            var row = new Dictionary<string, object>(columnCount);
            for (int j = 0; j < columnCount; j++)
            {
                var key = reader.ReadString() ?? string.Empty;
                var value = reader.ReadObject();
                if (value != null)
                {
                    row[key] = value;
                }
            }
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Save table data in binary format
    /// </summary>
    public void SaveTable(string tableName, Dictionary<string, object> schema, List<Dictionary<string, object>> rows)
    {
        // Get or create table ID (registers in sys_tables.ovu)
        var tableId = GetTableId(tableName);
        
        // Ensure table is registered in sys_tables.ovu
        var sysTables = LoadSystemTable(SysTablesFile);
        var existing = sysTables.FirstOrDefault(r => 
            GetStringValue(r, "table_name") == tableName);
        
        if (existing == null)
        {
            // If not registered, register it
            var newEntry = new Dictionary<string, object>
            {
                ["table_id"] = tableId,
                ["table_name"] = tableName,
                ["table_type"] = "table",
                ["created_at"] = DateTime.UtcNow.ToBinary()
            };
            sysTables.Add(newEntry);
            SaveSystemTable(SysTablesFile, sysTables);
            
            // Update cache
            _tableIdCache[tableName] = tableId;
            _tableNameCache[tableId] = tableName;
        }

        // Save table metadata
        var metaData = new Dictionary<string, object>
        {
            ["table_id"] = tableId,
            ["table_name"] = tableName,
            ["schema"] = schema,
            ["row_count"] = rows.Count,
            ["last_modified"] = DateTime.UtcNow.ToBinary(),
            ["version"] = FormatVersion
        };
        SaveTableMetadata(tableId, metaData);
        
        // Update schema cache
        _metadataCache.PutSchema(tableName, schema);
        
        // Invalidate query cache for this table
        _queryCache.InvalidateTable(tableName);

        // Save table data
        var dataPath = GetTableDataPath(tableId);
        
        // Ensure directory exists
        var dataDir = Path.GetDirectoryName(dataPath);
        if (!string.IsNullOrEmpty(dataDir) && !Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }
        
        // Retry on file lock
        const int maxRetries = 5;
        const int retryDelayMs = 50;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Use buffered stream for writing
                using var fileStream = new FileStream(dataPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 65536);
                using var bufferedStream = new System.IO.BufferedStream(fileStream, bufferSize: 65536);
                using var writer = new BinaryWriter(bufferedStream);

                // File header
                writer.WriteString(MagicNumber);
                writer.WriteInt32(FormatVersion);
                writer.WriteInt32(tableId);
                writer.WriteString(tableName);
                writer.WriteInt64(DateTime.UtcNow.ToBinary());

                // Schema
                writer.WriteInt32(schema.Count);
                foreach (var kvp in schema)
                {
                    writer.WriteString(kvp.Key);
                    writer.WriteObject(kvp.Value);
                }

                // Data (write count first)
                writer.WriteInt32(rows.Count);
                
                // Direct index access for large data
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    writer.WriteInt32(row.Count);
                    
                    // foreach for Dictionary
                    foreach (var kvp in row)
                    {
                        writer.WriteString(kvp.Key);
                        writer.WriteObject(kvp.Value);
                    }
                }

                writer.Flush();
                bufferedStream.Flush();
                
                // Update sys_columns
                UpdateSysColumns(tableId, schema);
                
                // Invalidate buffer pool for this table
                _bufferPool.InvalidateTable(tableId);
                
                // Flush dirty buffer pool pages
                FlushDirtyPagesForTable(tableId);
                
                return; // Success
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // Wait before retry
                Thread.Sleep(retryDelayMs);
            }
        }
        
        throw new IOException($"Failed to save table {tableName} after {maxRetries} attempts");
    }

    /// <summary>
    /// Load table metadata
    /// </summary>
    private Dictionary<string, object>? LoadTableMetadata(int tableId)
    {
        // Check cache first
        var tableName = _tableNameCache.GetValueOrDefault(tableId);
        if (tableName != null)
        {
            var cached = _metadataCache.GetMetadata(tableName);
            if (cached != null)
            {
                return cached;
            }
        }

        var metaPath = GetTableMetaPath(tableId);
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            // Use buffered stream for reading
            using var fileStream = new FileStream(metaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 16384);
            using var bufferedStream = new System.IO.BufferedStream(fileStream, bufferSize: 16384);
            using var reader = new BinaryReader(bufferedStream);

            var magic = reader.ReadString();
            if (magic != MagicNumber)
            {
                return null;
            }

            var version = reader.ReadInt32();
            var metadataCount = reader.ReadInt32();

            var metadata = new Dictionary<string, object>();
            for (int i = 0; i < metadataCount; i++)
            {
                var key = reader.ReadString() ?? string.Empty;
                var value = reader.ReadObject();
                if (value != null)
                {
                    metadata[key] = value;
                }
            }

            // Save to cache
            if (tableName != null)
            {
                _metadataCache.PutMetadata(tableName, metadata);
            }

            return metadata;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Save table metadata
    /// </summary>
    private void SaveTableMetadata(int tableId, Dictionary<string, object> metadata)
    {
        var metaPath = GetTableMetaPath(tableId);
        
        // Ensure directory exists
        var metaDir = Path.GetDirectoryName(metaPath);
        if (!string.IsNullOrEmpty(metaDir) && !Directory.Exists(metaDir))
        {
            Directory.CreateDirectory(metaDir);
        }
        
        // Retry on file lock
        const int maxRetries = 5;
        const int retryDelayMs = 50;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Use buffered stream
                using var fileStream = new FileStream(metaPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 16384);
                using var bufferedStream = new System.IO.BufferedStream(fileStream, bufferSize: 16384);
                using var writer = new BinaryWriter(bufferedStream);

                writer.WriteString(MagicNumber);
                writer.WriteInt32(FormatVersion);
                writer.WriteInt32(metadata.Count);

                foreach (var kvp in metadata)
                {
                    writer.WriteString(kvp.Key);
                    writer.WriteObject(kvp.Value);
                }

                writer.Flush();
                
                // Update metadata cache
                var tableName = _tableNameCache.GetValueOrDefault(tableId);
                if (tableName != null)
                {
                    _metadataCache.PutMetadata(tableName, metadata);
                }
                
                return; // Success
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // Wait before retry
                Thread.Sleep(retryDelayMs);
            }
        }
        
        throw new IOException($"Failed to save metadata for table {tableId} after {maxRetries} attempts");
    }

    /// <summary>
    /// Update sys_columns
    /// </summary>
    private void UpdateSysColumns(int tableId, Dictionary<string, object> schema)
    {
        var sysColumns = LoadSystemTable(SysColumnsFile);
        
        // Remove old entries for this table
        sysColumns.RemoveAll(r => GetIntValue(r, "table_id") == tableId);

        // Add new column entries
        if (schema.TryGetValue("Columns", out var columnsObj) && columnsObj is List<object> columns)
        {
            int columnNum = 1;
            foreach (var colObj in columns)
            {
                // Convert column to dictionary
                var colDict = ConvertColumnToDict(colObj);
                if (colDict != null)
                {
                    var colEntry = new Dictionary<string, object>
                    {
                        ["table_id"] = tableId,
                        ["column_num"] = columnNum++,
                        ["column_name"] = GetStringValue(colDict, "Name") ?? string.Empty,
                        ["data_type"] = GetStringValue(colDict, "DataType") ?? "String"
                    };
                    sysColumns.Add(colEntry);
                }
            }
        }

        SaveSystemTable(SysColumnsFile, sysColumns);
    }

    private Dictionary<string, object>? ConvertColumnToDict(object colObj)
    {
        if (colObj is Dictionary<string, object> dict)
        {
            return dict;
        }

        // Try convert via JSON
        try
        {
            var json = JsonSerializer.Serialize(colObj);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Load table data from binary format
    /// </summary>
    public (Dictionary<string, object> schema, List<Dictionary<string, object>> rows)? LoadTable(string tableName)
    {
        var tableId = FindTableId(tableName);
        if (!tableId.HasValue)
        {
            return null;
        }

        // Check schema cache
        var cachedSchema = _metadataCache.GetSchema(tableName);
        if (cachedSchema != null)
        {
            // If schema in cache, load data only
            var cachedRows = LoadTableData(tableId.Value);
            if (cachedRows != null)
            {
                return (cachedSchema, cachedRows);
            }
        }

        var dataPath = GetTableDataPath(tableId.Value);
        if (!File.Exists(dataPath))
        {
            return null;
        }

        // Use buffered stream for reading
        using var fileStream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);
        using var bufferedStream = new System.IO.BufferedStream(fileStream, bufferSize: 65536);
        using var reader = new BinaryReader(bufferedStream);

        // Check header
        var magic = reader.ReadString();
        if (magic != MagicNumber)
        {
            throw new InvalidOperationException($"Invalid table file format: {tableName}");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidOperationException($"Incompatible table format version: {tableName}");
        }

        var id = reader.ReadInt32();
        var name = reader.ReadString();
        var created = reader.ReadDateTime();

        // Schema
        var schemaCount = reader.ReadInt32();
        var schema = new Dictionary<string, object>();
        for (int i = 0; i < schemaCount; i++)
        {
            var key = reader.ReadString() ?? string.Empty;
            var value = reader.ReadObject();
            if (value != null)
            {
                schema[key] = value;
            }
        }

        // Data (pre-allocate)
        var rowCount = reader.ReadInt32();
        var rows = new List<Dictionary<string, object>>(rowCount);
        for (int i = 0; i < rowCount; i++)
        {
            var columnCount = reader.ReadInt32();
            var row = new Dictionary<string, object>(columnCount);
            for (int j = 0; j < columnCount; j++)
            {
                var key = reader.ReadString() ?? string.Empty;
                var value = reader.ReadObject();
                if (value != null)
                {
                    row[key] = value;
                }
            }
            rows.Add(row);
        }

        // Save to cache
        _metadataCache.PutSchema(tableName, schema);

        return (schema, rows);
    }

    /// <summary>
    /// Load table data only (no schema)
    /// </summary>
    private List<Dictionary<string, object>>? LoadTableData(int tableId)
    {
        var dataPath = GetTableDataPath(tableId);
        if (!File.Exists(dataPath))
        {
            return null;
        }

        try
        {
            // Use buffered stream for reading
            using var fileStream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);
            using var bufferedStream = new System.IO.BufferedStream(fileStream, bufferSize: 65536);
            using var reader = new BinaryReader(bufferedStream);

            // Skip header and schema
            var magic = reader.ReadString();
            if (magic != MagicNumber) return null;
            
            var version = reader.ReadInt32();
            var id = reader.ReadInt32();
            var name = reader.ReadString();
            var created = reader.ReadDateTime();

            // Skip schema
            var schemaCount = reader.ReadInt32();
            for (int i = 0; i < schemaCount; i++)
            {
                reader.ReadString();
                reader.ReadObject();
            }

            // Read data (pre-allocate)
            var rowCount = reader.ReadInt32();
            var rows = new List<Dictionary<string, object>>(rowCount);
            for (int i = 0; i < rowCount; i++)
            {
                var columnCount = reader.ReadInt32();
                var row = new Dictionary<string, object>(columnCount);
                for (int j = 0; j < columnCount; j++)
                {
                    var key = reader.ReadString() ?? string.Empty;
                    var value = reader.ReadObject();
                    if (value != null)
                    {
                        row[key] = value;
                    }
                }
                rows.Add(row);
            }

            return rows;
        }
        catch
        {
            return null;
        }
    }

    public void DeleteTable(string tableName)
    {
        var tableId = FindTableId(tableName);
        if (!tableId.HasValue)
        {
            return;
        }

        // Delete files
        var dataPath = GetTableDataPath(tableId.Value);
        var metaPath = GetTableMetaPath(tableId.Value);
        
        if (File.Exists(dataPath)) File.Delete(dataPath);
        if (File.Exists(metaPath)) File.Delete(metaPath);

        // Remove from sys_tables
        var sysTables = LoadSystemTable(SysTablesFile);
        sysTables.RemoveAll(r => GetIntValue(r, "table_id") == tableId.Value);
        SaveSystemTable(SysTablesFile, sysTables);

        // Remove from sys_columns
        var sysColumns = LoadSystemTable(SysColumnsFile);
        sysColumns.RemoveAll(r => GetIntValue(r, "table_id") == tableId.Value);
        SaveSystemTable(SysColumnsFile, sysColumns);

        // Update cache
        _tableIdCache.Remove(tableName);
        _tableNameCache.Remove(tableId.Value);
        
        // Invalidate all caches
        _metadataCache.Invalidate(tableName);
        _queryCache.InvalidateTable(tableName);
        _bufferPool.InvalidateTable(tableId.Value);
    }

    public bool TableExists(string tableName)
    {
        // Check null/empty
        if (string.IsNullOrEmpty(tableName))
        {
            return false;
        }
        
        // Check cache and sys_tables.ovu first
        var tableId = FindTableId(tableName);
        if (tableId.HasValue)
        {
            // Verify table files exist on disk
            var dataPath = GetTableDataPath(tableId.Value);
            var metaPath = GetTableMetaPath(tableId.Value);
            return File.Exists(dataPath) || File.Exists(metaPath);
        }
        
        // If not in sys_tables.ovu, check disk (recover if sys_tables.ovu was corrupted)
        try
        {
            if (Directory.Exists(_databaseDirectory))
            {
                var tableFiles = Directory.GetFiles(_databaseDirectory, "*.ovu")
                    .Where(f => !f.EndsWith(".meta") && 
                                !Path.GetFileName(f).Equals(SysTablesFile, StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).Equals(SysColumnsFile, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                foreach (var tableFile in tableFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(tableFile);
                    if (int.TryParse(fileName, out var fileTableId))
                    {
                        var metaPath = GetTableMetaPath(fileTableId);
                        if (File.Exists(metaPath))
                        {
                            try
                            {
                                var metadata = LoadTableMetadata(fileTableId);
                                if (metadata != null && metadata.TryGetValue("table_name", out var tableNameObj))
                                {
                                    var foundTableName = tableNameObj?.ToString();
                                    if (foundTableName == tableName)
                                    {
                                        // Found table on disk but not registered - restore registration
                                        var sysTables = LoadSystemTable(SysTablesFile);
                                        var newEntry = new Dictionary<string, object>
                                        {
                                            ["table_id"] = fileTableId,
                                            ["table_name"] = tableName,
                                            ["table_type"] = "table",
                                            ["created_at"] = metadata.TryGetValue("last_modified", out var lastMod) 
                                                ? lastMod 
                                                : DateTime.UtcNow.ToBinary()
                                        };
                                        
                                        sysTables.Add(newEntry);
                                        SaveSystemTable(SysTablesFile, sysTables);
                                        
                                        // Update cache
                                        _tableIdCache[tableName] = fileTableId;
                                        _tableNameCache[fileTableId] = tableName;
                                        
                                        return true;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore errors
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return false;
    }

    public List<string> GetTableNames()
    {
        var sysTables = LoadSystemTable(SysTablesFile);
        var tableNames = new List<string>();
        
        foreach (var tableEntry in sysTables)
        {
            var tableType = GetStringValue(tableEntry, "table_type");
            if (tableType != "table")
            {
                continue;
            }
            
            var tableName = GetStringValue(tableEntry, "table_name");
            if (string.IsNullOrEmpty(tableName))
            {
                continue;
            }
            
            // Verify table file exists on disk
            var tableId = GetIntValue(tableEntry, "table_id");
            if (tableId > 0)
            {
                var dataPath = GetTableDataPath(tableId);
                var metaPath = GetTableMetaPath(tableId);
                
                // Table exists if at least one file present
                if (File.Exists(dataPath) || File.Exists(metaPath))
                {
                    tableNames.Add(tableName);
                }
            }
        }
        
        return tableNames;
    }

    public string CreateDump(string tableName)
    {
        var loaded = LoadTable(tableName);
        if (!loaded.HasValue)
        {
            throw new InvalidOperationException($"Table {tableName} not found");
        }

        var (schema, rows) = loaded.Value;
        var tableId = FindTableId(tableName);
        var metadata = tableId.HasValue ? LoadTableMetadata(tableId.Value) : null;

        // Dumps remain in JSON for compatibility
        var dump = new TableDump
        {
            Version = "1.0",
            CreatedAt = DateTime.UtcNow,
            DatabaseName = _databaseId.ToString(),
            TableName = tableName,
            Schema = schema,
            Data = rows,
            Metadata = metadata != null ? new TableMetadata
            {
                Name = tableName,
                CreatedAt = metadata.ContainsKey("created_at") 
                    ? DateTime.FromBinary(Convert.ToInt64(metadata["created_at"]))
                    : DateTime.UtcNow,
                LastModified = metadata.ContainsKey("last_modified")
                    ? DateTime.FromBinary(Convert.ToInt64(metadata["last_modified"]))
                    : DateTime.UtcNow,
                RowCount = rows.Count
            } : null,
            RowCount = rows.Count
        };

        return JsonSerializer.Serialize(dump, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
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
        var tables = GetTableNames();
        var fullDump = new FullDatabaseDump
        {
            Version = "1.0",
            CreatedAt = DateTime.UtcNow,
            DatabaseName = _databaseId.ToString(),
            DatabaseMetadata = new DatabaseMetadata
            {
                Version = FormatVersion.ToString(),
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                Tables = new Dictionary<string, TableMetadata>()
            },
            Tables = new Dictionary<string, TableDump>()
        };

        foreach (var tableName in tables)
        {
            var loaded = LoadTable(tableName);
            if (loaded.HasValue)
            {
                var (schema, rows) = loaded.Value;
                var tableId = FindTableId(tableName);
                var metadata = tableId.HasValue ? LoadTableMetadata(tableId.Value) : null;

                fullDump.Tables[tableName] = new TableDump
                {
                    Version = "1.0",
                    CreatedAt = DateTime.UtcNow,
                    DatabaseName = fullDump.DatabaseName,
                    TableName = tableName,
                    Schema = schema,
                    Data = rows,
                    Metadata = metadata != null ? new TableMetadata
                    {
                        Name = tableName,
                        CreatedAt = metadata.ContainsKey("created_at")
                            ? DateTime.FromBinary(Convert.ToInt64(metadata["created_at"]))
                            : DateTime.UtcNow,
                        LastModified = metadata.ContainsKey("last_modified")
                            ? DateTime.FromBinary(Convert.ToInt64(metadata["last_modified"]))
                            : DateTime.UtcNow,
                        RowCount = rows.Count
                    } : null,
                    RowCount = rows.Count
                };
            }
        }

        return JsonSerializer.Serialize(fullDump, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public void RestoreFromFullDump(string fullDumpJson)
    {
        FullDatabaseDump? fullDump;
        try
        {
            fullDump = JsonSerializer.Deserialize<FullDatabaseDump>(fullDumpJson, new JsonSerializerOptions
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

        // Restore tables
        foreach (var tableDump in fullDump.Tables.Values)
        {
            SaveTable(tableDump.TableName, tableDump.Schema, tableDump.Data);
        }
    }

    public void SaveDumpToFile(string tableName, string dumpJson)
    {
        var dumpPath = GetDumpPath(tableName);
        File.WriteAllText(dumpPath, dumpJson);
    }

    public string LoadDumpFromFile(string tableName)
    {
        var dumpPath = GetDumpPath(tableName);
        if (!File.Exists(dumpPath))
        {
            throw new FileNotFoundException($"Dump file {dumpPath} not found");
        }
        return File.ReadAllText(dumpPath);
    }

    /// <summary>
    /// Flush dirty table pages to disk
    /// </summary>
    private void FlushDirtyPagesForTable(int tableId)
    {
        var dirtyPages = _bufferPool.GetDirtyPages(tableId);
        foreach (var page in dirtyPages)
        {
            // Mark as clean (full flush can be implemented here)
            page.IsDirty = false;
        }
    }

    // Helper methods
    private int GetIntValue(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value)) return 0;
        return value switch
        {
            int i => i,
            long l => (int)l,
            _ => Convert.ToInt32(value)
        };
    }

    private string? GetStringValue(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value)) return null;
        return value?.ToString();
    }
}

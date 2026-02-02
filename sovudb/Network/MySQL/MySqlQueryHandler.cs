using System.Text.Json;
using System.Text.RegularExpressions;
using ovudb.Core;
using ovudb.Network.Authentication;
using ovudb.OvuRequests;
using ovudb.SystemDatabase;

namespace ovudb.Network.MySQL;

/// <summary>
/// Handler for MySQL queries - converts MySQL queries to OvuDB queries
/// </summary>
public class MySqlQueryHandler
{
    private readonly Database? _database;
    private readonly ModelService? _modelService;
    private readonly AuthenticationService _authService;
    private readonly SystemDatabaseService? _systemDatabaseService;
    private readonly Dictionary<string, Database> _databases;
    private readonly Func<string, Database?> _getDatabaseFunc;

    public MySqlQueryHandler(
        Database? database,
        ModelService? modelService,
        AuthenticationService authService,
        SystemDatabaseService? systemDatabaseService,
        Dictionary<string, Database> databases,
        Func<string, Database?> getDatabaseFunc)
    {
        _database = database;
        _modelService = modelService;
        _authService = authService;
        _systemDatabaseService = systemDatabaseService;
        _databases = databases;
        _getDatabaseFunc = getDatabaseFunc;
    }

    /// <summary>
    /// Handle MySQL query
    /// </summary>
    public async Task<MySqlQueryResult> HandleQueryAsync(string query, MySqlConnection connection, CancellationToken cancellationToken = default)
    {
        var trimmedQuery = query.Trim();
        if (string.IsNullOrEmpty(trimmedQuery))
        {
            return new MySqlQueryResult { Success = false, ErrorMessage = "Empty query" };
        }

        // Handle special MySQL commands
        var upperQuery = trimmedQuery.ToUpperInvariant();
        
        // SHOW DATABASES
        if (upperQuery == "SHOW DATABASES" || upperQuery.StartsWith("SHOW DATABASES"))
        {
            return await HandleShowDatabasesAsync(connection, cancellationToken);
        }
        
        // SHOW TABLES
        if (upperQuery == "SHOW TABLES" || upperQuery.StartsWith("SHOW TABLES"))
        {
            return await HandleShowTablesAsync(connection, cancellationToken);
        }
        
        // CREATE DATABASE
        var createDbMatch = Regex.Match(trimmedQuery, @"^\s*CREATE\s+DATABASE\s+(?:IF\s+NOT\s+EXISTS\s+)?([^\s;]+)", RegexOptions.IgnoreCase);
        if (createDbMatch.Success)
        {
            return await HandleCreateDatabaseAsync(createDbMatch.Groups[1].Value.Trim('`', '"', '\''), connection, cancellationToken);
        }
        
        // USE database
        var useMatch = Regex.Match(trimmedQuery, @"^\s*USE\s+([^\s;]+)", RegexOptions.IgnoreCase);
        if (useMatch.Success)
        {
            var dbName = useMatch.Groups[1].Value.Trim('`', '"', '\'');
            connection.SetDatabase(dbName);
            return new MySqlQueryResult { Success = true, AffectedRows = 0 };
        }
        
        // SELECT DATABASE()
        if (upperQuery == "SELECT DATABASE()" || upperQuery.StartsWith("SELECT DATABASE()"))
        {
            return await HandleSelectDatabaseAsync(connection, cancellationToken);
        }
        
        // SELECT VERSION()
        if (upperQuery == "SELECT VERSION()" || upperQuery.StartsWith("SELECT VERSION()"))
        {
            return await HandleSelectVersionAsync(connection, cancellationToken);
        }
        
        // SELECT USER()
        if (upperQuery == "SELECT USER()" || upperQuery.StartsWith("SELECT USER()"))
        {
            return await HandleSelectUserAsync(connection, cancellationToken);
        }
        
        // SET NAMES - just return OK, no actual processing needed
        var setNamesMatch = Regex.Match(trimmedQuery, @"^\s*SET\s+NAMES\s+", RegexOptions.IgnoreCase);
        if (setNamesMatch.Success)
        {
            return new MySqlQueryResult { Success = true, AffectedRows = 0 };
        }
        
        // SET @@session.*, SET @@global.*, SET @@local.* - system variables, just return OK
        var setSessionMatch = Regex.Match(trimmedQuery, @"^\s*SET\s+(@@(session|global|local)\.|@@)", RegexOptions.IgnoreCase);
        if (setSessionMatch.Success)
        {
            return new MySqlQueryResult { Success = true, AffectedRows = 0 };
        }
        
        // SET character_set_client, SET character_set_connection, etc. - just return OK
        var setCharsetMatch = Regex.Match(trimmedQuery, @"^\s*SET\s+(character_set_|collation_|sql_mode|time_zone|autocommit|transaction)", RegexOptions.IgnoreCase);
        if (setCharsetMatch.Success)
        {
            return new MySqlQueryResult { Success = true, AffectedRows = 0 };
        }

        // Try to execute as OvuDB query
        // Some queries don't require a database (like SELECT 1, SELECT VERSION(), etc.)
        var db = _database ?? (connection.CurrentDatabase != null ? _getDatabaseFunc(connection.CurrentDatabase) : null);
        
        // Check if query requires a database
        // Simple SELECT queries like SELECT 1, SELECT 2, etc. don't require a database
        var simpleSelectMatch = Regex.Match(trimmedQuery, @"^\s*SELECT\s+(\d+|'[^']*'|""[^""]*""|NULL|VERSION\(\)|DATABASE\(\)|USER\(\)|NOW\(\)|CURRENT_TIMESTAMP\(\)|CURRENT_DATE\(\)|CURRENT_TIME\(\))\s*(,|FROM|WHERE|GROUP|ORDER|LIMIT|$)", RegexOptions.IgnoreCase);
        if (simpleSelectMatch.Success && db == null)
        {
            // Extract the value from the SELECT statement
            var valueMatch = Regex.Match(trimmedQuery, @"^\s*SELECT\s+(\d+)", RegexOptions.IgnoreCase);
            var value = valueMatch.Success ? int.Parse(valueMatch.Groups[1].Value) : 1;
            
            // Simple SELECT without FROM - return a dummy result
            return new MySqlQueryResult
            {
                Success = true,
                Columns = new[] { new MySqlColumn { Name = value.ToString(), Type = ColumnType.MYSQL_TYPE_LONG, Length = 11, Flags = 0, Decimals = 0 } },
                Rows = new List<object?[]> { new object?[] { value } }
            };
        }
        
        if (db == null)
        {
            return new MySqlQueryResult 
            { 
                Success = false, 
                ErrorMessage = "No database selected. Use USE statement to select a database." 
            };
        }

        try
        {
            // Parse and execute query
            var parser = new Parser(trimmedQuery);
            var queryNode = parser.Parse();
            
            var optimizer = new Optimizer(db);
            var optimizedQuery = optimizer.Optimize(queryNode);
            
            var executor = new Executor(db, _modelService);
            var result = executor.Execute(optimizedQuery);
            
            return ConvertOvuDbResultToMySql(result);
        }
        catch (Exception ex)
        {
            return new MySqlQueryResult 
            { 
                Success = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private async Task<MySqlQueryResult> HandleShowDatabasesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var databases = new List<string>();
        
        // Get databases from system database
        if (_systemDatabaseService != null)
        {
            try
            {
                var dbTable = _systemDatabaseService.GetDbTable();
                dbTable.Reload();
                var dbEntries = dbTable.GetAll()
                    .Where(db => db.User == "*")
                    .Select(db => db.Db)
                    .Distinct()
                    .Where(db => !string.IsNullOrEmpty(db))
                    .ToList();
                
                databases.AddRange(dbEntries);
            }
            catch { }
        }
        
        // Add databases from in-memory dictionary
        foreach (var dbName in _databases.Keys)
        {
            if (!databases.Contains(dbName, StringComparer.OrdinalIgnoreCase))
            {
                databases.Add(dbName);
            }
        }
        
        // Add system database
        if (!databases.Contains("ovusys", StringComparer.OrdinalIgnoreCase))
        {
            databases.Add("ovusys");
        }
        
        databases.Sort();
        
        return new MySqlQueryResult
        {
            Success = true,
            Columns = new[] { new MySqlColumn { Name = "Database", Type = ColumnType.MYSQL_TYPE_VAR_STRING } },
            Rows = databases.Select(db => new object?[] { db }).ToList()
        };
    }

    private async Task<MySqlQueryResult> HandleShowTablesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var db = _database ?? (connection.CurrentDatabase != null ? _getDatabaseFunc(connection.CurrentDatabase) : null);
        if (db == null)
        {
            return new MySqlQueryResult 
            { 
                Success = false, 
                ErrorMessage = "No database selected" 
            };
        }
        
        var tables = db.GetTableNames();
        
        return new MySqlQueryResult
        {
            Success = true,
            Columns = new[] { new MySqlColumn { Name = $"Tables_in_{connection.CurrentDatabase ?? "database"}", Type = ColumnType.MYSQL_TYPE_VAR_STRING } },
            Rows = tables.Select(table => new object?[] { table }).ToList()
        };
    }

    private async Task<MySqlQueryResult> HandleSelectDatabaseAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        return new MySqlQueryResult
        {
            Success = true,
            Columns = new[] { new MySqlColumn { Name = "DATABASE()", Type = ColumnType.MYSQL_TYPE_VAR_STRING } },
            Rows = new List<object?[]> { new object?[] { connection.CurrentDatabase ?? "NULL" } }
        };
    }

    private async Task<MySqlQueryResult> HandleSelectVersionAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        return new MySqlQueryResult
        {
            Success = true,
            Columns = new[] { new MySqlColumn { Name = "VERSION()", Type = ColumnType.MYSQL_TYPE_VAR_STRING } },
            Rows = new List<object?[]> { new object?[] { "5.7.0-OvuDB" } }
        };
    }

    private async Task<MySqlQueryResult> HandleSelectUserAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        return new MySqlQueryResult
        {
            Success = true,
            Columns = new[] { new MySqlColumn { Name = "USER()", Type = ColumnType.MYSQL_TYPE_VAR_STRING } },
            Rows = new List<object?[]> { new object?[] { connection.Username ?? "unknown" } }
        };
    }

    private async Task<MySqlQueryResult> HandleCreateDatabaseAsync(string databaseName, MySqlConnection connection, CancellationToken cancellationToken)
    {
        // Check if database exists
        if (_databases.ContainsKey(databaseName))
        {
            return new MySqlQueryResult 
            { 
                Success = false, 
                ErrorMessage = $"Can't create database '{databaseName}'; database exists" 
            };
        }

        // Check reserved names
        if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase) || 
            databaseName.Equals("ovudb_system", StringComparison.OrdinalIgnoreCase))
        {
            return new MySqlQueryResult 
            { 
                Success = false, 
                ErrorMessage = $"Cannot create database named '{databaseName}' - reserved name" 
            };
        }

        // Create database
        var database = new Database(databaseName, dataDirectory: null);
        _databases[databaseName] = database;

        return new MySqlQueryResult 
        { 
            Success = true, 
            AffectedRows = 1,
            Message = $"Database '{databaseName}' created"
        };
    }

    private MySqlQueryResult ConvertOvuDbResultToMySql(object result)
    {
        if (result == null)
        {
            return new MySqlQueryResult { Success = true, AffectedRows = 0 };
        }

        // Try to deserialize as JSON
        JsonElement jsonElement;
        if (result is JsonElement je)
        {
            jsonElement = je;
        }
        else
        {
            try
            {
                var json = JsonSerializer.Serialize(result);
                jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch
            {
                return new MySqlQueryResult 
                { 
                    Success = true, 
                    AffectedRows = 1,
                    Message = result.ToString() ?? "OK"
                };
            }
        }

        // Check for SELECT result (rows)
        if (jsonElement.TryGetProperty("rows", out var rows))
        {
            var rowList = new List<object?[]>();
            var columns = new List<MySqlColumn>();

            if (rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0)
            {
                // Get columns from first row
                var firstRow = rows[0];
                if (firstRow.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in firstRow.EnumerateObject())
                    {
                        columns.Add(new MySqlColumn 
                        { 
                            Name = prop.Name, 
                            Type = ColumnType.MYSQL_TYPE_VAR_STRING 
                        });
                    }
                }

                // Extract rows
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind == JsonValueKind.Object)
                    {
                        var rowValues = new List<object?>();
                        foreach (var col in columns)
                        {
                            if (row.TryGetProperty(col.Name, out var value))
                            {
                                rowValues.Add(ConvertJsonValue(value));
                            }
                            else
                            {
                                rowValues.Add(null);
                            }
                        }
                        rowList.Add(rowValues.ToArray());
                    }
                }
            }

            return new MySqlQueryResult
            {
                Success = true,
                Columns = columns.ToArray(),
                Rows = rowList
            };
        }

        // Check for message (INSERT, UPDATE, DELETE, etc.)
        if (jsonElement.TryGetProperty("message", out var message))
        {
            return new MySqlQueryResult 
            { 
                Success = true, 
                AffectedRows = 1,
                Message = message.GetString() ?? "OK"
            };
        }

        // Check for affected rows
        if (jsonElement.TryGetProperty("affectedRows", out var affectedRows))
        {
            return new MySqlQueryResult 
            { 
                Success = true, 
                AffectedRows = affectedRows.GetInt32()
            };
        }

        return new MySqlQueryResult { Success = true, AffectedRows = 0 };
    }

    private object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetInt64(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.GetRawText(),
            JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText()
        };
    }
}

/// <summary>
/// MySQL query result
/// </summary>
public class MySqlQueryResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int AffectedRows { get; set; }
    public long LastInsertId { get; set; }
    public string? Message { get; set; }
    public MySqlColumn[]? Columns { get; set; }
    public List<object?[]>? Rows { get; set; }
}

/// <summary>
/// MySQL column definition
/// </summary>
public class MySqlColumn
{
    public string Name { get; set; } = string.Empty;
    public ColumnType Type { get; set; }
    public int Length { get; set; } = 255;
    public int Flags { get; set; }
    public int Decimals { get; set; }
}


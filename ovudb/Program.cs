using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ovudb.Network;

namespace ovudb;

internal class Program
{
    private static TcpClient? _tcpClient;
    private static NetworkStream? _stream;
    private static StreamReader? _reader;
    private static StreamWriter? _writer;
    private static string? _currentDatabase;
    private static string? _username;
    private static string _host = "localhost";
    private static int _port = 47015;

    static async Task Main(string[] args)
    {
        // Parse command-line arguments
        var queryArgs = ParseArguments(args);

        try
        {
            // Connect to server
            await ConnectAsync();

            // Authenticate
            if (!await AuthenticateAsync())
            {
                Console.WriteLine("Authentication error");
                Environment.Exit(1);
                return;
            }

            // If command specified, execute and exit
            if (queryArgs.Count > 0)
            {
                var query = string.Join(" ", queryArgs);
                await ExecuteQueryAsync(query);
                return;
            }

            // Interactive mode
            await InteractiveModeAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        finally
        {
            Disconnect();
        }
    }

    static List<string> ParseArguments(string[] args)
    {
        var queryArgs = new List<string>();
        var skipNext = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--host":
                    if (i + 1 < args.Length)
                    {
                        _host = args[++i];
                        skipNext = true;
                    }
                    break;
                case "-P":
                case "--port":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int port))
                    {
                        _port = port;
                        skipNext = true;
                    }
                    break;
                case "-u":
                case "--user":
                case "--username":
                    if (i + 1 < args.Length)
                    {
                        _username = args[++i];
                        skipNext = true;
                    }
                    break;
                case "-p":
                case "--password":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        // Password from argument (insecure, for compatibility)
                        Environment.SetEnvironmentVariable("OVUDB_PASSWORD", args[++i]);
                        skipNext = true;
                    }
                    else
                    {
                        Console.Write("Password: ");
                        var password = ReadPassword();
                        Console.WriteLine();
                        Environment.SetEnvironmentVariable("OVUDB_PASSWORD", password);
                    }
                    break;
                case "--help":
                case "-?":
                    ShowUsage();
                    Environment.Exit(0);
                    break;
                default:
                    // Everything else is query
                    queryArgs.Add(arg);
                    break;
            }
        }

        return queryArgs;
    }

    static void ShowUsage()
    {
        Console.WriteLine("Usage: ovudb [OPTIONS] [QUERY]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --host HOST       Server host (default: localhost)");
        Console.WriteLine("  -P, --port PORT       Server port (default: 47015)");
        Console.WriteLine("  -u, --user USER       Username");
        Console.WriteLine("  -p, --password        Prompt for password");
        Console.WriteLine("  --help, -?            Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ovudb -u admin -p");
        Console.WriteLine("  ovudb -h localhost -P 47015 -u admin");
        Console.WriteLine("  ovudb -u admin \"SELECT * FROM users;\"");
    }

    static async Task ConnectAsync()
    {
        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_host, _port);
            _stream = _tcpClient.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8);
            _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
            Console.WriteLine($"Connected to server {_host}:{_port}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to connect to server {_host}:{_port}: {ex.Message}");
        }
    }

    static async Task<bool> AuthenticateAsync()
    {
        if (_writer == null || _reader == null)
            return false;

        // Prompt for username if not specified
        if (string.IsNullOrEmpty(_username))
        {
            Console.Write("Username: ");
            _username = Console.ReadLine();
        }

        // Prompt for password
        string? password = Environment.GetEnvironmentVariable("OVUDB_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        // Send authentication request
        var authRequest = new Request
        {
            Command = "AUTH",
            Parameters = new Dictionary<string, object>
            {
                ["username"] = _username,
                ["password"] = password
            }
        };

        var json = JsonSerializer.Serialize(authRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await _writer.WriteLineAsync(json);

        // Read response
        var responseLine = await _reader.ReadLineAsync();
        if (string.IsNullOrEmpty(responseLine))
            return false;

        var response = JsonSerializer.Deserialize<Response>(responseLine, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (response?.Success == true)
        {
            Console.WriteLine($"Welcome, {_username}!");
            return true;
        }
        else
        {
            Console.WriteLine($"Authentication error: {response?.Error ?? "Unknown error"}");
            return false;
        }
    }

    static async Task InteractiveModeAsync()
    {
        Console.WriteLine("OvuDB CLI. Type 'help' for help, 'exit' to quit.");
        Console.WriteLine();

        var queryBuffer = new StringBuilder();
        bool inMultiLine = false;

        while (true)
        {
            try
            {
                string prompt = _currentDatabase != null ? $"ovudb [{_currentDatabase}]> " : "ovudb> ";
                Console.Write(prompt);

                var line = Console.ReadLine();
                if (line == null)
                    break;

                line = line.Trim();

                // Handle special commands
                if (!inMultiLine && IsSpecialCommand(line, out var command, out var args))
                {
                    await HandleSpecialCommandAsync(command, args);
                    continue;
                }

                // Add line to buffer
                if (!string.IsNullOrEmpty(line))
                {
                    queryBuffer.Append(line);
                    queryBuffer.Append(' ');

                    // Check if query ends with semicolon
                    if (line.EndsWith(';'))
                    {
                        var query = queryBuffer.ToString().TrimEnd(' ', ';');
                        if (!string.IsNullOrEmpty(query))
                        {
                            await ExecuteQueryAsync(query);
                        }
                        queryBuffer.Clear();
                        inMultiLine = false;
                    }
                    else
                    {
                        inMultiLine = true;
                    }
                }
                else if (inMultiLine)
                {
                    // Empty line in multiline mode - continue
                    queryBuffer.Append('\n');
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                queryBuffer.Clear();
                inMultiLine = false;
            }
        }
    }

    static bool IsSpecialCommand(string line, out string command, out string[] args)
    {
        command = string.Empty;
        args = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(line))
            return false;

        // Remove trailing semicolon if present
        line = line.TrimEnd(';');

        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var cmd = parts[0].ToUpperInvariant();
        
        // Special commands (not SQL/ovuRequests). CREATE/DROP only for DATABASE, rest as queries
        if (cmd == "HELP" || cmd == "\\H" || cmd == "\\?" ||
            cmd == "EXIT" || cmd == "QUIT" || cmd == "\\Q" ||
            cmd == "USE" || cmd == "\\U" ||
            cmd == "UNUSE" ||
            cmd == "SHOW" ||
            cmd == "CLEAR" || cmd == "\\C" ||
            cmd == "STATUS" || cmd == "\\S")
        {
            command = cmd;
            // Remove semicolon from arguments if present
            args = parts.Skip(1).Select(arg => arg.TrimEnd(';')).ToArray();
            return true;
        }
        
        // CREATE and DROP - only for DATABASE, rest pass as queries
        if (cmd == "CREATE" && parts.Length > 1 && parts[1].ToUpperInvariant() == "DATABASE")
        {
            command = cmd;
            args = parts.Skip(1).Select(arg => arg.TrimEnd(';')).ToArray();
            return true;
        }
        
        if (cmd == "DROP" && parts.Length > 1 && parts[1].ToUpperInvariant() == "DATABASE")
        {
            command = cmd;
            args = parts.Skip(1).Select(arg => arg.TrimEnd(';')).ToArray();
            return true;
        }

        return false;
    }

    static async Task HandleSpecialCommandAsync(string command, string[] args)
    {
        switch (command)
        {
            case "HELP":
            case "\\H":
            case "\\?":
                ShowHelp();
                break;

            case "EXIT":
            case "QUIT":
            case "\\Q":
                Environment.Exit(0);
                break;

            case "USE":
            case "\\U":
                if (args.Length > 0)
                {
                    // Remove semicolon from database name if present
                    var dbName = args[0].TrimEnd(';').Trim();
                    if (!string.IsNullOrEmpty(dbName))
                    {
                        await ExecuteCommandAsync("USE", new Dictionary<string, object> { ["database"] = dbName });
                    }
                    else
                    {
                        Console.WriteLine("Usage: USE <database_name>");
                    }
                }
                else
                {
                    Console.WriteLine("Usage: USE <database_name>");
                }
                break;

            case "UNUSE":
                await ExecuteCommandAsync("UNUSE", null);
                break;

            case "SHOW":
                if (args.Length > 0 && args[0].ToUpperInvariant() == "DATABASES")
                {
                    await ExecuteCommandAsync("SHOW DATABASES", null);
                }
                else if (args.Length > 0 && args[0].ToUpperInvariant() == "TABLES")
                {
                    await ExecuteCommandAsync("SHOW TABLES", null);
                }
                else
                {
                    Console.WriteLine("Usage: SHOW DATABASES | SHOW TABLES");
                }
                break;

            case "CREATE":
                if (args.Length > 0 && args[0].ToUpperInvariant() == "DATABASE")
                {
                    if (args.Length > 1)
                    {
                        var dbName = args[1].TrimEnd(';').Trim();
                        await ExecuteCommandAsync("CREATE DATABASE", new Dictionary<string, object> { ["database"] = dbName });
                    }
                    else
                    {
                        Console.WriteLine("Usage: CREATE DATABASE <database_name>");
                    }
                }
                else
                {
                    Console.WriteLine("Usage: CREATE DATABASE <database_name>");
                }
                break;

            case "DROP":
                if (args.Length > 0 && args[0].ToUpperInvariant() == "DATABASE")
                {
                    if (args.Length > 1)
                    {
                        var dbName = args[1].TrimEnd(';').Trim();
                        await ExecuteCommandAsync("DROP DATABASE", new Dictionary<string, object> { ["database"] = dbName });
                    }
                    else
                    {
                        Console.WriteLine("Usage: DROP DATABASE <database_name>");
                    }
                }
                else
                {
                    Console.WriteLine("Usage: DROP DATABASE <database_name>");
                }
                break;

            case "CLEAR":
            case "\\C":
                Console.Clear();
                break;

            case "STATUS":
            case "\\S":
                ShowStatus();
                break;

            default:
                Console.WriteLine($"Unknown command: {command}");
                break;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Available commands:");
        Console.WriteLine("  help, \\h, \\?           - Show this help");
        Console.WriteLine("  exit, quit, \\q         - Exit client");
        Console.WriteLine("  use <db>, \\u           - Select database");
        Console.WriteLine("  unuse                  - Deselect current database");
        Console.WriteLine("  show databases          - List databases");
        Console.WriteLine("  show tables             - List tables");
        Console.WriteLine("  create database <name>  - Create database");
        Console.WriteLine("  drop database <name>   - Drop database");
        Console.WriteLine("  clear, \\c              - Clear screen");
        Console.WriteLine("  status, \\s             - Show connection status");
        Console.WriteLine();
        Console.WriteLine("ovuRequests queries:");
        Console.WriteLine("  SELECT * FROM table_name;");
        Console.WriteLine("  SELECT * FROM table_name WHERE column = value;");
        Console.WriteLine();
        Console.WriteLine("Create tables:");
        Console.WriteLine("  CREATE TABLE table_name (");
        Console.WriteLine("    column1 INTEGER PRIMARY KEY AUTOINCREMENT,");
        Console.WriteLine("    column2 STRING NOT NULL,");
        Console.WriteLine("    column3 DOUBLE");
        Console.WriteLine("  );");
        Console.WriteLine();
        Console.WriteLine("  Or table is created automatically on first INSERT.");
        Console.WriteLine();
        Console.WriteLine("Insert data:");
        Console.WriteLine("  INSERT INTO table_name (col1, col2) VALUES (val1, 'val2');");
        Console.WriteLine("  INSERT INTO table_name VALUES (val1, 'val2', val3);");
        Console.WriteLine();
        Console.WriteLine("Update and delete:");
        Console.WriteLine("  UPDATE table_name SET column = value WHERE condition;");
        Console.WriteLine("  DELETE FROM table_name WHERE condition;");
        Console.WriteLine();
        Console.WriteLine("Drop tables:");
        Console.WriteLine("  DROP TABLE table_name;");
        Console.WriteLine();
        Console.WriteLine("Models (table templates):");
        Console.WriteLine("  MODEL ADD name {field:type:key, field2:type} perm|temp");
        Console.WriteLine("  MODEL LIST");
        Console.WriteLine("  MODEL SEE name");
        Console.WriteLine();
    }

    static void ShowStatus()
    {
        Console.WriteLine();
        Console.WriteLine($"Server: {_host}:{_port}");
        Console.WriteLine($"User: {_username ?? "unknown"}");
        Console.WriteLine($"Database: {_currentDatabase ?? "none selected"}");
        Console.WriteLine($"Connection: {(_tcpClient?.Connected == true ? "active" : "inactive")}");
        Console.WriteLine();
    }

    static async Task ExecuteQueryAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        // Remove trailing semicolon if present
        var trimmedQuery = query.Trim().TrimEnd(';');

        // Check if special command (USE, SHOW, etc.)
        if (IsSpecialCommand(trimmedQuery, out var command, out var args))
        {
            await HandleSpecialCommandAsync(command, args);
            return;
        }

        // Execute as QUERY
        await ExecuteCommandAsync("QUERY", new Dictionary<string, object> { ["query"] = trimmedQuery });
    }

    static async Task ExecuteCommandAsync(string command, Dictionary<string, object>? parameters)
    {
        if (_writer == null || _reader == null)
        {
            Console.WriteLine("Not connected to server");
            return;
        }

        try
        {
            var request = new Request
            {
                Command = command,
                Parameters = parameters,
                Database = _currentDatabase
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await _writer.WriteLineAsync(json);

            // Read response
            var responseLine = await _reader.ReadLineAsync();
            if (string.IsNullOrEmpty(responseLine))
            {
                Console.WriteLine("Empty response from server");
                return;
            }

            var response = JsonSerializer.Deserialize<Response>(responseLine, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (response == null)
            {
                Console.WriteLine("Failed to parse server response");
                return;
            }

            if (!response.Success)
            {
                // For USE command show clearer error
                if (command == "USE" && response.Error != null && response.Error.Contains("already selected"))
                {
                    Console.WriteLine($"Error: {response.Error}");
                    Console.WriteLine("Use UNUSE to deselect current database.");
                }
                else
                {
                    Console.WriteLine($"Error: {response.Error}");
                }
                return;
            }

            // Handle result
            await DisplayResultAsync(response.Data, command);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command execution error: {ex.Message}");
        }
    }

    static async Task DisplayResultAsync(object? data, string command)
    {
        if (data == null)
        {
            Console.WriteLine("OK");
            return;
        }

        // Handle special commands
        if (command == "USE")
        {
            if (data is JsonElement jsonElement)
            {
                string? dbName = null;
                
                // Try to get database name from response first (priority)
                if (jsonElement.TryGetProperty("database", out var dbProp))
                {
                    dbName = dbProp.GetString()?.Trim();
                }
                
                // Output message
                if (jsonElement.TryGetProperty("message", out var message))
                {
                    var msg = message.GetString() ?? "";
                    Console.WriteLine(msg);
                    
                    // If no database field, try to extract from message
                    if (string.IsNullOrEmpty(dbName) && msg.Contains("changed to", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = msg.Split(new[] { "changed to" }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1)
                        {
                            dbName = parts[1].Trim();
                            // Remove semicolon if present
                            dbName = dbName.TrimEnd(';').Trim();
                        }
                    }
                }
                
                // Update current database if name extracted
                if (!string.IsNullOrEmpty(dbName))
                {
                    _currentDatabase = dbName;
                }
            }
            return;
        }

        if (command == "UNUSE")
        {
            if (data is JsonElement jsonElement && jsonElement.TryGetProperty("message", out var message))
            {
                var msg = message.GetString() ?? "";
                Console.WriteLine(msg);
                // Clear current database
                _currentDatabase = null;
            }
            return;
        }

        if (command == "CREATE DATABASE" || command == "DROP DATABASE")
        {
            if (data is JsonElement jsonElement && jsonElement.TryGetProperty("message", out var message))
            {
                Console.WriteLine(message.GetString());
            }
            else if (data is JsonElement errorJson && errorJson.TryGetProperty("error", out var error))
            {
                Console.WriteLine($"Error: {error.GetString()}");
            }
            return;
        }

        if (command == "SHOW DATABASES")
        {
            if (data is JsonElement jsonElement && jsonElement.TryGetProperty("databases", out var databases))
            {
                Console.WriteLine();
                Console.WriteLine("+------------------+");
                Console.WriteLine("| Database         |");
                Console.WriteLine("+------------------+");
                foreach (var db in databases.EnumerateArray())
                {
                    var dbName = db.GetString() ?? "";
                    Console.WriteLine($"| {dbName,-16} |");
                }
                Console.WriteLine("+------------------+");
                Console.WriteLine();
            }
            return;
        }

        if (command == "SHOW TABLES")
        {
            if (data is JsonElement jsonElement && jsonElement.TryGetProperty("tables", out var tables))
            {
                Console.WriteLine();
                Console.WriteLine("+------------------+");
                Console.WriteLine("| Tables           |");
                Console.WriteLine("+------------------+");
                foreach (var table in tables.EnumerateArray())
                {
                    var tableName = table.GetString() ?? "";
                    Console.WriteLine($"| {tableName,-16} |");
                }
                Console.WriteLine("+------------------+");
                Console.WriteLine();
            }
            return;
        }

        // Handle query results (QUERY)
        if (data is JsonElement resultJson)
        {
            // Check for "rows" field (SELECT result)
            if (resultJson.TryGetProperty("rows", out var rows))
            {
                await DisplayTableAsync(rows);
            }
            else if (resultJson.TryGetProperty("message", out var message))
            {
                Console.WriteLine(message.GetString());
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    static async Task DisplayTableAsync(JsonElement rows)
    {
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
        {
            Console.WriteLine("Empty result");
            return;
        }

        var rowList = new List<Dictionary<string, string>>();
        var columnOrder = new List<string>(); // Preserve column order
        var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect rows and columns, preserve order from first row
        bool isFirstRow = true;
        foreach (var row in rows.EnumerateArray())
        {
            var rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in row.EnumerateObject())
            {
                var key = prop.Name;
                var value = FormatValue(prop.Value);
                rowDict[key] = value;
                
                // Preserve column order from first row
                if (isFirstRow && !allColumns.Contains(key))
                {
                    columnOrder.Add(key);
                    allColumns.Add(key);
                }
                else if (!isFirstRow && !allColumns.Contains(key))
                {
                    // If new columns in later rows, append them
                    columnOrder.Add(key);
                    allColumns.Add(key);
                }
            }
            rowList.Add(rowDict);
            isFirstRow = false;
        }

        if (columnOrder.Count == 0)
        {
            Console.WriteLine("Empty result");
            return;
        }

        var columns = columnOrder; // Use preserved order
        var columnWidths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Compute column widths
        foreach (var col in columns)
        {
            var maxWidth = Math.Max(col.Length, rowList.Max(r => r.GetValueOrDefault(col, "").Length));
            columnWidths[col] = Math.Min(maxWidth, 50); // Cap max width
        }

        // Check if table too wide for console
        var totalWidth = columns.Sum(c => columnWidths[c] + 3) + 1; // +3 separators, +1 last
        var consoleWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 120;
        
        // If table too wide, use vertical layout
        if (totalWidth > consoleWidth && rowList.Count <= 10)
        {
            DisplayTableVertically(rowList, columns, columnWidths);
            return;
        }

        // Print header
        Console.WriteLine();
        var headerSeparator = "+" + string.Join("+", columns.Select(c => new string('-', columnWidths[c] + 2))) + "+";
        Console.WriteLine(headerSeparator);
        Console.WriteLine("| " + string.Join(" | ", columns.Select(c => c.PadRight(columnWidths[c]))) + " |");
        Console.WriteLine(headerSeparator);

        // Print rows
        foreach (var row in rowList)
        {
            var values = columns.Select(c =>
            {
                var val = row.GetValueOrDefault(c, "");
                if (val.Length > columnWidths[c])
                    val = val.Substring(0, columnWidths[c] - 3) + "...";
                return val.PadRight(columnWidths[c]);
            });
            Console.WriteLine("| " + string.Join(" | ", values) + " |");
        }

        Console.WriteLine(headerSeparator);
        Console.WriteLine($"{rowList.Count} row(s)");
        Console.WriteLine();
    }

    static void DisplayTableVertically(List<Dictionary<string, string>> rowList, List<string> columns, Dictionary<string, int> columnWidths)
    {
        Console.WriteLine();
        for (int i = 0; i < rowList.Count; i++)
        {
            var row = rowList[i];
            Console.WriteLine($"*** Row {i + 1} ***");
            foreach (var col in columns)
            {
                var val = row.GetValueOrDefault(col, "");
                if (val.Length > 60)
                    val = val.Substring(0, 57) + "...";
                Console.WriteLine($"  {col.PadRight(20)}: {val}");
            }
            if (i < rowList.Count - 1)
                Console.WriteLine();
        }
        Console.WriteLine();
        Console.WriteLine($"{rowList.Count} row(s)");
        Console.WriteLine();
    }

    static string FormatValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return "NULL";
            case JsonValueKind.String:
                return value.GetString() ?? "";
            case JsonValueKind.Number:
                return value.GetRawText();
            case JsonValueKind.True:
                return "TRUE";
            case JsonValueKind.False:
                return "FALSE";
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return JsonSerializer.Serialize(value);
            default:
                return value.GetRawText();
        }
    }

    static string ReadPassword()
    {
        var password = new StringBuilder();
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(true);

            if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
            else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
        }
        while (key.Key != ConsoleKey.Enter);

        return password.ToString();
    }

    static void Disconnect()
    {
        try
        {
            _reader?.Dispose();
            _writer?.Dispose();
            _stream?.Dispose();
            _tcpClient?.Dispose();
        }
        catch
        {
            // Ignore errors on close
        }
    }
}

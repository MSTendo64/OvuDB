using System.Net;
using System.Net.Sockets;
using ovudb.Core;
using ovudb.Network.Authentication;
using ovudb.OvuRequests;
using ovudb.Storage;
using ovudb.SystemDatabase;
using ovudb.SystemDatabase.Models;

namespace ovudb.Network;

/// <summary>
/// OvuDB server for handling network connections.
/// </summary>
public class OvuDbServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ConnectionPool _connectionPool;
    private readonly AuthenticationService _authService;
    private readonly ModelService _modelService;
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly Dictionary<string, Database> _databases = new();
    private readonly Dictionary<string, string> _connectionDatabases = new(); // Current DB per connection
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _isRunning;
    private readonly int _port;
    private readonly string _dataDirectory;

    public OvuDbServer(int port = 47015, string dataDirectory = "data", int maxConnections = 100, int idleTimeoutMinutes = 30)
    {
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port), 
                port, 
                $"Port must be between 1 and 65535. Got: {port}"
            );
        }

        _port = port;
        _dataDirectory = dataDirectory;
        
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port), 
                port, 
                $"Invalid port: {port}. Port must be between 1 and 65535"
            );
        }
        
        _connectionPool = new ConnectionPool(maxConnections: maxConnections, idleTimeout: TimeSpan.FromMinutes(idleTimeoutMinutes));
        _authService = new AuthenticationService(dataDirectory);
        
        // Initialize SystemDatabaseService and ModelService
        _systemDatabaseService = new SystemDatabaseService(Path.Combine(dataDirectory, "ovusys"));
        _modelService = new ModelService(_systemDatabaseService);
        
        // Load database mapping and existing databases from disk on startup
        LoadExistingDatabases();
    }
    
    /// <summary>
    /// Load existing databases from disk
    /// </summary>
    private void LoadExistingDatabases()
    {
        try
        {
            if (!Directory.Exists(_dataDirectory))
            {
                return;
            }
            
            var dbTable = _systemDatabaseService.GetDbTable();
            dbTable.Reload();
            var dbEntries = dbTable.GetAll()
                .Where(db => db.User == "*") // User="*" denotes database existence
                .Select(db => db.Db)
                .Distinct()
                .ToList();
            
            foreach (var dbName in dbEntries)
            {
                if (string.IsNullOrEmpty(dbName) || dbName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                // Check if database exists on disk
                if (DatabaseExistsOnDisk(dbName))
                {
                    // Load database into memory
                    if (!_databases.ContainsKey(dbName))
                    {
                        try
                        {
                            var databaseId = GetDatabaseIdOnDisk(dbName);
                            Console.WriteLine($"Loading database {dbName} with ID {databaseId}");
                            
                            // Create BinaryStorage with correct ID
                            var storage = new BinaryStorage(_dataDirectory, databaseId);
                            var database = new Database(dbName, storage: storage, dataDirectory: _dataDirectory);
                            _databases[dbName] = database;
                            Console.WriteLine($"Loaded database: {dbName}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error loading database {dbName}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: database {dbName} is registered but not found on disk");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to load databases from disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Register database in system db table
    /// </summary>
    private void RegisterDatabaseInSystemDb(string databaseName)
    {
        try
        {
            var dbTable = _systemDatabaseService.GetDbTable();
            dbTable.Reload();
            
            // Check if User="*" entry already exists for this database
            var existing = dbTable.GetAll()
                .FirstOrDefault(db => db.Db == databaseName && db.User == "*");
            
            if (existing == null)
            {
                // Create User="*" entry to denote database existence
                var dbEntry = new SystemDb
                {
                    Host = "%",
                    Db = databaseName,
                    User = "*",
                    SelectPriv = true,
                    InsertPriv = true,
                    UpdatePriv = true,
                    DeletePriv = true,
                    CreatePriv = true,
                    DropPriv = true,
                    GrantPriv = false,
                    ReferencesPriv = true,
                    IndexPriv = true,
                    AlterPriv = true
                };
                dbTable.Insert(dbEntry);
                dbTable.Flush();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to register database {databaseName}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Unregister database from system db table
    /// </summary>
    private void UnregisterDatabaseFromSystemDb(string databaseName)
    {
        try
        {
            var dbTable = _systemDatabaseService.GetDbTable();
            dbTable.Reload();
            
            // Remove all entries for this database
            var entries = dbTable.GetAll()
                .Where(db => db.Db == databaseName)
                .ToList();
            
            foreach (var entry in entries)
            {
                dbTable.Delete(entry);
            }
            
            dbTable.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to unregister database {databaseName}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Calculate database ID from name
    /// </summary>
    private int CalculateDatabaseId(string databaseName)
    {
        if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        return Math.Abs(databaseName.GetHashCode()) % 1000000 + 1000;
    }
    
    /// <summary>
    /// Register database in system db table
    /// </summary>
    private void RegisterDatabase(string databaseName)
    {
        RegisterDatabaseInSystemDb(databaseName);
    }

    /// <summary>
    /// Start server
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("Server is already running");
        }

        try
        {
            _listener.Start();
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"Failed to start server on port {_port}. Port may be in use. {ex.Message}", ex);
        }

        _isRunning = true;

        // Start background cleanup of idle connections
        _ = Task.Run(async () => await CleanupConnectionsLoopAsync(_cancellationTokenSource.Token));

        Console.WriteLine($"OvuDB server started on port {_port}");

        // Main accept loop
        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(async () => await HandleClientAsync(tcpClient, _cancellationTokenSource.Token));
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting connection: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Stop server
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cancellationTokenSource.Cancel();
        _listener.Stop();
        _connectionPool.Dispose();
        Console.WriteLine("OvuDB server stopped");
    }

    /// <summary>
    /// Handle client connection
    /// </summary>
    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        Connection? connection = null;
        try
        {
            connection = new Connection(tcpClient);
            
            if (!_connectionPool.AddConnection(connection))
            {
                await connection.SendErrorAsync("Connection limit exceeded");
                return;
            }

            Console.WriteLine($"New connection: {connection.ConnectionId}");

            // Wait for authentication
            var authenticated = await AuthenticateClientAsync(connection, cancellationToken);
            if (!authenticated)
            {
                return;
            }

            // Process requests
            await ProcessRequestsAsync(connection, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client handling error: {ex.Message}");
        }
        finally
        {
            if (connection != null)
            {
                _connectionPool.RemoveConnection(connection.ConnectionId);
                _connectionDatabases.Remove(connection.ConnectionId);
                Console.WriteLine($"Connection closed: {connection.ConnectionId}");
            }
        }
    }

    /// <summary>
    /// Authenticate client
    /// </summary>
    private async Task<bool> AuthenticateClientAsync(Connection connection, CancellationToken cancellationToken)
    {
        try
        {
            var request = await connection.ReceiveRequestAsync(cancellationToken);
            if (request == null || request.Command != "AUTH")
            {
                await connection.SendErrorAsync("Authentication required");
                return false;
            }

            var username = request.Parameters?.GetValueOrDefault("username")?.ToString();
            var password = request.Parameters?.GetValueOrDefault("password")?.ToString();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                await connection.SendErrorAsync("Invalid credentials");
                return false;
            }

            if (_authService.Authenticate(username, password, out var user))
            {
                connection.Authenticate(username);
                await connection.SendSuccessAsync(new { message = "Authentication successful", username });
                Console.WriteLine($"User {username} authenticated (connection {connection.ConnectionId})");
                return true;
            }
            else
            {
                await connection.SendErrorAsync("Invalid credentials");
                return false;
            }
        }
        catch (Exception ex)
        {
            await connection.SendErrorAsync($"Authentication error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Handle client requests
    /// </summary>
    private async Task ProcessRequestsAsync(Connection connection, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && connection.IsConnected)
        {
            try
            {
                var request = await connection.ReceiveRequestAsync(cancellationToken);
                if (request == null)
                {
                    break;
                }

                await ProcessRequestAsync(connection, request, cancellationToken);
            }
            catch (Exception ex)
            {
                await connection.SendErrorAsync($"Request handling error: {ex.Message}");
                break;
            }
        }
    }

    /// <summary>
    /// Handle single request
    /// </summary>
    private async Task ProcessRequestAsync(Connection connection, Request request, CancellationToken cancellationToken)
    {
        if (!connection.IsAuthenticated)
        {
            await connection.SendErrorAsync("Authentication required");
            return;
        }

        try
        {
            var result = request.Command.ToUpperInvariant() switch
            {
                "USE" => await HandleUseDatabaseAsync(connection, request),
                "UNUSE" => await HandleUnuseDatabaseAsync(connection),
                "SHOW DATABASES" => await HandleShowDatabasesAsync(connection),
                "SHOW TABLES" => await HandleShowTablesAsync(connection, request),
                "CREATE DATABASE" => await HandleCreateDatabaseAsync(connection, request),
                "DROP DATABASE" => await HandleDropDatabaseAsync(connection, request),
                "QUERY" => await HandleQueryAsync(connection, request),
                "INSERT" => await HandleInsertAsync(connection, request),
                "UPDATE" => await HandleUpdateAsync(connection, request),
                "DELETE" => await HandleDeleteAsync(connection, request),
                "CREATE_TABLE" => await HandleCreateTableAsync(connection, request),
                "DROP_TABLE" => await HandleDropTableAsync(connection, request),
                "GET_TABLES" => await HandleGetTablesAsync(connection, request),
                "PING" => await HandlePingAsync(connection),
                _ => new { error = $"Unknown command: {request.Command}" }
            };

            await connection.SendSuccessAsync(result);
        }
        catch (Exception ex)
        {
            await connection.SendErrorAsync($"Command execution error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get or create database
    /// </summary>
    private Database GetOrCreateDatabase(string databaseName)
    {
        if (!_databases.TryGetValue(databaseName, out var database))
        {
            // Check if database exists on disk
            if (DatabaseExistsOnDisk(databaseName))
            {
                database = new Database(databaseName, dataDirectory: _dataDirectory);
                _databases[databaseName] = database;
            }
            else
            {
                database = new Database(databaseName, dataDirectory: _dataDirectory);
                _databases[databaseName] = database;
            }
        }
        return database;
    }

    /// <summary>
    /// Find database ID on disk by scanning directories
    /// </summary>
    private int? FindDatabaseIdOnDisk(string databaseName)
    {
        try
        {
            if (!Directory.Exists(_dataDirectory))
            {
                return null;
            }

            // First check calculated ID
            var calculatedId = CalculateDatabaseId(databaseName);
            var calculatedDirectory = Path.Combine(_dataDirectory, calculatedId.ToString());
            if (Directory.Exists(calculatedDirectory))
            {
                var files = Directory.GetFiles(calculatedDirectory);
                if (files.Length > 0)
                {
                    Console.WriteLine($"Debug: database {databaseName} found by calculated ID: {calculatedId}");
                    return calculatedId;
                }
            }

            // If registered but calculated ID wrong, scan for directory with files (except system)
            bool isRegistered = false;
            try
            {
                var dbTable = _systemDatabaseService.GetDbTable();
                dbTable.Reload();
                isRegistered = dbTable.GetAll()
                    .Any(db => db.User == "*" && db.Db != null && db.Db.Equals(databaseName, StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            if (isRegistered)
            {
                // If database registered, find directory with files
                var directories = Directory.GetDirectories(_dataDirectory);
                foreach (var dir in directories)
                {
                    var dirName = Path.GetFileName(dir);
                    if (int.TryParse(dirName, out var dirId))
                    {
                        // Skip system database
                        if (dirId == 1 && !databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // Check if directory has files
                        var files = Directory.GetFiles(dir);
                        if (files.Length > 0)
                        {
                            Console.WriteLine($"Debug: database {databaseName} registered, found directory ID {dirId} with {files.Length} files");
                            // Return first found ID with files
                            return dirId;
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Debug: error finding database {databaseName} on disk: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if database exists on disk
    /// </summary>
    private bool DatabaseExistsOnDisk(string databaseName)
    {
        var foundId = FindDatabaseIdOnDisk(databaseName);
        return foundId.HasValue;
    }

    /// <summary>
    /// Get database ID on disk (scan all directories)
    /// </summary>
    private int GetDatabaseIdOnDisk(string databaseName)
    {
        var foundId = FindDatabaseIdOnDisk(databaseName);
        if (foundId.HasValue)
        {
            return foundId.Value;
        }
        
        // If not found, return calculated ID (for new database creation)
        return CalculateDatabaseId(databaseName);
    }

    /// <summary>
    /// Get current database for connection
    /// </summary>
    private Database? GetCurrentDatabase(Connection connection)
    {
        if (_connectionDatabases.TryGetValue(connection.ConnectionId, out var databaseName))
        {
            // If system database ovusys, return from SystemDatabaseService
            if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
            {
                return _systemDatabaseService.SystemDatabase;
            }
            return GetOrCreateDatabase(databaseName);
        }
        return null;
    }

    /// <summary>
    /// Handle USE command (select database)
    /// </summary>
    private async Task<object> HandleUseDatabaseAsync(Connection connection, Request request)
    {
        var databaseName = request.Database ?? request.Parameters?.GetValueOrDefault("database")?.ToString();
        
        if (string.IsNullOrEmpty(databaseName))
        {
            throw new ArgumentException("Database name not specified");
        }

        // Trim invalid characters (semicolon, trailing spaces)
        databaseName = databaseName.Trim().TrimEnd(';').Trim();

        // Ensure database name not empty after trim
        if (string.IsNullOrEmpty(databaseName))
        {
            throw new ArgumentException("Database name cannot be empty");
        }

        // Check if database already selected
        if (_connectionDatabases.TryGetValue(connection.ConnectionId, out var currentDatabase))
        {
            // If selecting same database
            if (currentDatabase.Equals(databaseName, StringComparison.OrdinalIgnoreCase))
            {
                return new { message = $"Database {databaseName} already selected", database = databaseName };
            }
            
            throw new InvalidOperationException($"Database {currentDatabase} is already selected. Run UNUSE first to deselect it.");
        }

        // Check user database access
        var username = connection.Username;
        if (!string.IsNullOrEmpty(username))
        {
            var user = _authService.GetUser(username);
            if (user != null && !_authService.HasDatabaseAccess(user, databaseName))
            {
                throw new UnauthorizedAccessException($"User {username} has no access to database {databaseName}");
            }
        }

        // Check if database exists (ovusys always available)
        if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
        {
            // System database handled via SystemDatabaseService
            _connectionDatabases[connection.ConnectionId] = databaseName;
            return new { message = $"Database changed to {databaseName}", database = databaseName };
        }
        
        // For regular databases check existence (in memory first)
        if (!_databases.ContainsKey(databaseName))
        {
            // Check if database registered in system db table
            bool existsInSystemDb = false;
            try
            {
                var dbTable = _systemDatabaseService.GetDbTable();
                dbTable.Reload();
                existsInSystemDb = dbTable.GetAll()
                    .Any(db => db.User == "*" && db.Db != null && db.Db.Equals(databaseName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning checking database in system table: {ex.Message}");
            }
            
            // Check on disk
            var calculatedId = CalculateDatabaseId(databaseName);
            Console.WriteLine($"Debug USE: database {databaseName}, calculated ID: {calculatedId}, path: {Path.Combine(_dataDirectory, calculatedId.ToString())}");
            bool existsOnDisk = DatabaseExistsOnDisk(databaseName);
            Console.WriteLine($"Debug USE: existsInSystemDb={existsInSystemDb}, existsOnDisk={existsOnDisk}");
            
            // If database does not exist in system table or on disk
            if (!existsInSystemDb && !existsOnDisk)
            {
                throw new InvalidOperationException($"Database {databaseName} does not exist. Use CREATE DATABASE to create it.");
            }
            
            // If registered but not on disk, check if directory exists with different content
            if (existsInSystemDb && !existsOnDisk)
            {
                // Extra check: directory may exist but check failed
                var checkDatabaseId = CalculateDatabaseId(databaseName);
                var databaseDirectory = Path.Combine(_dataDirectory, checkDatabaseId.ToString());
                if (Directory.Exists(databaseDirectory))
                {
                    var files = Directory.GetFiles(databaseDirectory);
                    var dirs = Directory.GetDirectories(databaseDirectory);
                    Console.WriteLine($"Debug: directory {databaseDirectory} exists. Files: {files.Length}, subdirs: {dirs.Length}");
                    // If directory exists and has content, consider database exists
                    if (files.Length > 0 || dirs.Length > 0)
                    {
                        Console.WriteLine($"Debug: directory not empty, considering database exists");
                        existsOnDisk = true;
                    }
                }
                
                if (!existsOnDisk)
                {
                    throw new InvalidOperationException($"Database {databaseName} is registered but its directory was not found on disk. Use DROP DATABASE to remove the record or restore data.");
                }
            }
            
            // Database must exist on disk; load it (do not create new). Use found ID, not calculated (GetHashCode may vary).
            var databaseId = GetDatabaseIdOnDisk(databaseName);
            Console.WriteLine($"Debug USE: loading database {databaseName} with ID {databaseId}");
            
            // Create BinaryStorage with correct ID
            var storage = new BinaryStorage(_dataDirectory, databaseId);
            var database = new Database(databaseName, storage: storage, dataDirectory: _dataDirectory);
            _databases[databaseName] = database;
            
            // Register database in system db if not yet (e.g. created manually on disk)
            if (!existsInSystemDb)
            {
                RegisterDatabase(databaseName);
            }
        }
        
        // Set current database for connection
        _connectionDatabases[connection.ConnectionId] = databaseName;
        
        return new { message = $"Database changed to {databaseName}", database = databaseName };
    }

    /// <summary>
    /// Handle UNUSE command (deselect database)
    /// </summary>
    private async Task<object> HandleUnuseDatabaseAsync(Connection connection)
    {
        if (_connectionDatabases.TryGetValue(connection.ConnectionId, out var currentDatabase))
        {
            _connectionDatabases.Remove(connection.ConnectionId);
            return new { message = $"Database {currentDatabase} deselected" };
        }
        
        return new { message = "No database was selected" };
    }

    /// <summary>
    /// Handle SHOW DATABASES command
    /// </summary>
    private async Task<object> HandleShowDatabasesAsync(Connection connection)
    {
        var databases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Load databases from system db table
        try
        {
            var dbTable = _systemDatabaseService.GetDbTable();
            dbTable.Reload();
            var dbEntries = dbTable.GetAll()
                .Where(db => db.User == "*") // User="*" denotes database existence
                .Select(db => db.Db)
                .Distinct()
                .ToList();
            
            foreach (var dbName in dbEntries)
            {
                if (!string.IsNullOrEmpty(dbName))
                {
                    databases.Add(dbName);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning loading database list: {ex.Message}");
        }
        
        // Add all databases from dictionary (no semicolons etc.)
        foreach (var dbName in _databases.Keys)
        {
            var cleanName = dbName.Trim().TrimEnd(';').Trim();
            if (!string.IsNullOrEmpty(cleanName))
            {
                databases.Add(cleanName);
            }
        }
        
        // Add system database ovusys if not already
        databases.Add("ovusys");
        
        // Sort database list
        var sortedDatabases = databases.ToList();
        sortedDatabases.Sort();
        
        return new { databases = sortedDatabases };
    }

    /// <summary>
    /// Handle SHOW TABLES command
    /// </summary>
    private async Task<object> HandleShowTablesAsync(Connection connection, Request request)
    {
        var database = GetCurrentDatabase(connection);
        if (database == null)
        {
            var databaseName = request.Database ?? "default";
            // If system database ovusys, use SystemDatabaseService
            if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
            {
                database = _systemDatabaseService.SystemDatabase;
            }
            else
            {
                database = GetOrCreateDatabase(databaseName);
            }
        }
        
        var tables = database.GetTableNames();
        return new { tables };
    }

    /// <summary>
    /// Handle CREATE DATABASE command
    /// </summary>
    private async Task<object> HandleCreateDatabaseAsync(Connection connection, Request request)
    {
        var databaseName = request.Database ?? request.Parameters?.GetValueOrDefault("database")?.ToString();
        
        if (string.IsNullOrEmpty(databaseName))
        {
            throw new ArgumentException("Database name not specified");
        }

        // Check if database exists in memory
        if (_databases.ContainsKey(databaseName))
        {
            throw new InvalidOperationException($"Database {databaseName} already exists");
        }

        // Check if database exists on disk
        if (DatabaseExistsOnDisk(databaseName))
        {
            throw new InvalidOperationException($"Database {databaseName} already exists");
        }

        // Check reserved name
        if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase) || 
            databaseName.Equals("ovudb_system", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot create database named {databaseName} - reserved name");
        }

        var database = new Database(databaseName, dataDirectory: _dataDirectory);
        _databases[databaseName] = database;
        
        // Register database in mapping
        RegisterDatabase(databaseName);
        
        // Add entry to system db table for database existence
        RegisterDatabaseInSystemDb(databaseName);
        
        return new { message = $"Database {databaseName} created" };
    }

    /// <summary>
    /// Handle DROP DATABASE command
    /// </summary>
    private async Task<object> HandleDropDatabaseAsync(Connection connection, Request request)
    {
        var databaseName = request.Database ?? request.Parameters?.GetValueOrDefault("database")?.ToString();
        
        if (string.IsNullOrEmpty(databaseName))
        {
            throw new ArgumentException("Database name not specified");
        }

        if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase) || 
            databaseName.Equals("ovudb_system", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cannot drop system database");
        }

        if (!_databases.TryGetValue(databaseName, out var database))
        {
            throw new InvalidOperationException($"Database {databaseName} not found");
        }

        _databases.Remove(databaseName);
        
        // Remove entry from system db table
        UnregisterDatabaseFromSystemDb(databaseName);
        
        // Remove from connection current databases
        var keysToRemove = _connectionDatabases
            .Where(kvp => kvp.Value == databaseName)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            _connectionDatabases.Remove(key);
        }
        
        return new { message = $"Database {databaseName} dropped" };
    }

    private async Task<object> HandleQueryAsync(Connection connection, Request request)
    {
        var queryText = request.Parameters?.GetValueOrDefault("query")?.ToString();
        
        if (string.IsNullOrEmpty(queryText))
        {
            throw new ArgumentException("Query text not specified");
        }

        // Get current database
        var database = GetCurrentDatabase(connection);
        if (database == null)
        {
            throw new InvalidOperationException("No database selected. Use USE to select a database");
        }

        try
        {
            // Parse query
            var parser = new Parser(queryText);
            var queryNode = parser.Parse();

            // Optimize query
            var optimizer = new Optimizer(database);
            var optimizedQuery = optimizer.Optimize(queryNode);

            // Execute query
            var executor = new Executor(database, _modelService);
            var result = executor.Execute(optimizedQuery);

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Query execution error: {ex.Message}", ex);
        }
    }

    private async Task<object> HandleInsertAsync(Connection connection, Request request)
    {
        // Insert stub
        return new { message = "Insert command requires implementation" };
    }

    private async Task<object> HandleUpdateAsync(Connection connection, Request request)
    {
        // Update stub
        return new { message = "Update command requires implementation" };
    }

    private async Task<object> HandleDeleteAsync(Connection connection, Request request)
    {
        // Delete stub
        return new { message = "Delete command requires implementation" };
    }

    private async Task<object> HandleCreateTableAsync(Connection connection, Request request)
    {
        var databaseName = request.Database ?? "default";
        var tableName = request.Table ?? throw new ArgumentException("Table name is required");
        
        var database = GetOrCreateDatabase(databaseName);
        // Table creation via reflection or other mechanism to be implemented
        
        return new { message = $"Table {tableName} created in database {databaseName}" };
    }

    private async Task<object> HandleDropTableAsync(Connection connection, Request request)
    {
        var databaseName = request.Database ?? "default";
        var tableName = request.Table ?? throw new ArgumentException("Table name is required");
        
        if (_databases.TryGetValue(databaseName, out var database))
        {
            database.DropTable(tableName);
            return new { message = $"Table {tableName} dropped from database {databaseName}" };
        }
        
        return new { error = $"Database {databaseName} not found" };
    }

    private async Task<object> HandleGetTablesAsync(Connection connection, Request request)
    {
        var databaseName = request.Database ?? "default";
        
        if (_databases.TryGetValue(databaseName, out var database))
        {
            var tables = database.GetTableNames();
            return new { tables };
        }
        
        return new { tables = new List<string>() };
    }

    private async Task<object> HandlePingAsync(Connection connection)
    {
        return new { message = "pong", timestamp = DateTime.UtcNow };
    }

    /// <summary>
    /// Idle connection cleanup loop
    /// </summary>
    private async Task CleanupConnectionsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                _connectionPool.CleanupIdleConnections();
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cancellationTokenSource.Dispose();
    }
}

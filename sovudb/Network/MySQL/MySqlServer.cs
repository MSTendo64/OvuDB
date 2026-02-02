using System.Net;
using System.Net.Sockets;
using ovudb.Core;
using ovudb.Network.Authentication;
using ovudb.SystemDatabase;
using ovudb.Storage;

namespace ovudb.Network.MySQL;

/// <summary>
/// MySQL-compatible server for OvuDB
/// </summary>
public class MySqlServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ConnectionPool _connectionPool;
    private readonly AuthenticationService _authService;
    private readonly ModelService _modelService;
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly Dictionary<string, Database> _databases = new();
    private readonly Dictionary<string, string> _connectionDatabases = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _isRunning;
    private readonly int _port;
    private readonly string _dataDirectory;

    public MySqlServer(
        int port = 3306,
        string dataDirectory = "data",
        int maxConnections = 100,
        int idleTimeoutMinutes = 30)
    {
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535");
        }

        _port = port;
        _dataDirectory = dataDirectory;

        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Invalid port");
        }

        _connectionPool = new ConnectionPool(maxConnections: maxConnections, idleTimeout: TimeSpan.FromMinutes(idleTimeoutMinutes));
        _authService = new AuthenticationService(dataDirectory);
        _systemDatabaseService = new SystemDatabaseService(Path.Combine(dataDirectory, "ovusys"));
        _modelService = new ModelService(_systemDatabaseService);

        LoadExistingDatabases();
    }

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
                .Where(db => db.User == "*")
                .Select(db => db.Db)
                .Distinct()
                .ToList();

            foreach (var dbName in dbEntries)
            {
                if (string.IsNullOrEmpty(dbName) || dbName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

            if (DatabaseExistsOnDisk(dbName))
            {
                if (!_databases.ContainsKey(dbName))
                {
                    try
                    {
                        var databaseId = GetDatabaseIdOnDisk(dbName);
                        var storage = new BinaryStorage(_dataDirectory, databaseId);
                        var db = new Database(dbName, storage: storage, dataDirectory: _dataDirectory);
                        _databases[dbName] = db;
                        Console.WriteLine($"Loaded database: {dbName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading database {dbName}: {ex.Message}");
                    }
                }
            }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to load databases from disk: {ex.Message}");
        }
    }

    private int CalculateDatabaseId(string databaseName)
    {
        if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        return Math.Abs(databaseName.GetHashCode()) % 1000000 + 1000;
    }

    private bool DatabaseExistsOnDisk(string databaseName)
    {
        var foundId = FindDatabaseIdOnDisk(databaseName);
        return foundId.HasValue;
    }

    private int? FindDatabaseIdOnDisk(string databaseName)
    {
        try
        {
            if (!Directory.Exists(_dataDirectory))
            {
                return null;
            }

            var calculatedId = CalculateDatabaseId(databaseName);
            var calculatedDirectory = Path.Combine(_dataDirectory, calculatedId.ToString());
            if (Directory.Exists(calculatedDirectory))
            {
                var files = Directory.GetFiles(calculatedDirectory);
                if (files.Length > 0)
                {
                    return calculatedId;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private int GetDatabaseIdOnDisk(string databaseName)
    {
        var foundId = FindDatabaseIdOnDisk(databaseName);
        if (foundId.HasValue)
        {
            return foundId.Value;
        }
        return CalculateDatabaseId(databaseName);
    }

    private Database? GetDatabase(string databaseName)
    {
        if (databaseName.Equals("ovusys", StringComparison.OrdinalIgnoreCase))
        {
            return _systemDatabaseService.SystemDatabase;
        }

        if (_databases.TryGetValue(databaseName, out var database))
        {
            return database;
        }

        if (DatabaseExistsOnDisk(databaseName))
        {
            try
            {
                var databaseId = GetDatabaseIdOnDisk(databaseName);
                var storage = new BinaryStorage(_dataDirectory, databaseId);
                var db = new Database(databaseName, storage: storage, dataDirectory: _dataDirectory);
                _databases[databaseName] = db;
                return db;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Start MySQL server
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
            Console.WriteLine($"[MySQL] TcpListener started on port {_port}");
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[MySQL] ERROR: Failed to start TcpListener: {ex.Message}");
            throw new InvalidOperationException($"Failed to start MySQL server on port {_port}. Port may be in use. {ex.Message}", ex);
        }

        _isRunning = true;
        Console.WriteLine($"MySQL-compatible server started on port {_port}");

        // Start background cleanup
        _ = Task.Run(async () => await CleanupConnectionsLoopAsync(_cancellationTokenSource.Token));

        // Main accept loop
        Console.WriteLine($"[MySQL] Server listening on port {_port}, waiting for connections...");
        
        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                Console.WriteLine($"[MySQL] Waiting for new connection...");
                var tcpClient = await _listener.AcceptTcpClientAsync();
                Console.WriteLine($"[MySQL] ✓ Accepted new connection from {tcpClient.Client.RemoteEndPoint}");
                Console.WriteLine($"[MySQL] Connection details: Connected={tcpClient.Connected}, NoDelay={tcpClient.NoDelay}");
                
                // Start handling in background task
                var task = Task.Run(async () => 
                {
                    try
                    {
                        await HandleClientAsync(tcpClient, _cancellationTokenSource.Token);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MySQL] Error in HandleClientAsync task: {ex.Message}");
                        Console.WriteLine($"[MySQL] Stack trace: {ex.StackTrace}");
                    }
                });
                
                // Don't await, let it run in background
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine($"[MySQL] Listener disposed, exiting accept loop");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySQL] Error in accept loop: {ex.Message}");
                Console.WriteLine($"[MySQL] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[MySQL] Stack trace: {ex.StackTrace}");
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
        Console.WriteLine("MySQL server stopped");
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        MySqlConnection? connection = null;
        try
        {
            Console.WriteLine($"[MySQL] New client connection from {tcpClient.Client.RemoteEndPoint}");
            connection = new MySqlConnection(tcpClient);

            if (!_connectionPool.AddConnection(connection, connection.ConnectionId))
            {
                Console.WriteLine($"[MySQL] Connection limit exceeded for {connection.ConnectionId}");
                await MySqlProtocol.SendErrorPacketAsync(connection, 1040, "Too many connections", cancellationToken: cancellationToken);
                return;
            }

            Console.WriteLine($"[MySQL] New MySQL connection: {connection.ConnectionId}");

            // Send handshake
            Console.WriteLine($"[MySQL] Sending handshake to {connection.ConnectionId}");
            await MySqlProtocol.SendHandshakeAsync(connection, cancellationToken);
            Console.WriteLine($"[MySQL] Handshake sent to {connection.ConnectionId}");

            // Read handshake response (client should respond immediately)
            Console.WriteLine($"[MySQL] Waiting for handshake response from {connection.ConnectionId}");
            try
            {
                // Add a small delay to ensure handshake is fully received by client
                await Task.Delay(10, cancellationToken);
                
                var handshakeResponse = await MySqlProtocol.ReadHandshakeResponseAsync(connection, cancellationToken);
                Console.WriteLine($"[MySQL] Handshake response received from {connection.ConnectionId}, username: {handshakeResponse.Username}");

                // Authenticate
                // Note: MySQL uses mysql_native_password authentication
                // The client sends a hashed password using SHA1(SHA1(password)) XOR SHA1(salt + SHA1(SHA1(password)))
                // For simplicity in this implementation, we'll try common passwords
                // In production, should implement proper MySQL native password authentication
                
                Console.WriteLine($"[MySQL] Authentication attempt for user: {handshakeResponse.Username}");
                Console.WriteLine($"[MySQL] Auth response length: {handshakeResponse.AuthResponse?.Length ?? 0}");
                Console.WriteLine($"[MySQL] Client requested auth plugin: {handshakeResponse.AuthPluginName ?? "none"}");
                
                   // Check if user exists
                   var existingUser = _authService.GetUser(handshakeResponse.Username);
                   if (existingUser == null)
                   {
                       Console.WriteLine($"[MySQL] User '{handshakeResponse.Username}' not found in system");
                   }
                   else
                   {
                       Console.WriteLine($"[MySQL] User '{handshakeResponse.Username}' found in system");
                   }
                   
                   // Try authentication with common passwords (for testing)
                   // In production, implement proper mysql_native_password hash verification
                   var authenticated = false;
                   User? user = null;
                   
                   // If client requested caching_sha2_password but server sent mysql_native_password,
                   // client should use mysql_native_password for authentication
                   // If auth response is empty, it might mean client will send password later, but
                   // for mysql_native_password, password should be in handshake response
                   if (handshakeResponse.AuthResponse == null || handshakeResponse.AuthResponse.Length == 0)
                   {
                       Console.WriteLine($"[MySQL] Empty auth response - trying default password 'admin'");
                       // Empty password - try default password
                       authenticated = _authService.Authenticate(handshakeResponse.Username, "admin", out user);
                   }
                   else
                   {
                       // Client sent auth response - for mysql_native_password, this is the hashed password
                       // For now, we'll just try the default password
                       // In production, should verify mysql_native_password hash
                       Console.WriteLine($"[MySQL] Auth response provided ({handshakeResponse.AuthResponse.Length} bytes) - trying default password 'admin'");
                       authenticated = _authService.Authenticate(handshakeResponse.Username, "admin", out user);
                   }
                
                if (!authenticated)
                {
                    Console.WriteLine($"[MySQL] Authentication with password 'admin' failed");
                }
                
                // Log authentication result
                if (authenticated)
                {
                    Console.WriteLine($"[MySQL] Authentication successful for user: {handshakeResponse.Username}");
                }
                else
                {
                    Console.WriteLine($"[MySQL] Authentication failed for user: {handshakeResponse.Username}");
                    Console.WriteLine($"[MySQL] Note: Make sure user '{handshakeResponse.Username}' exists with password 'admin'");
                }

                if (authenticated)
                {
                    connection.Authenticate(handshakeResponse.Username);
                    
                    Console.WriteLine($"[MySQL] Sending OK packet after authentication...");
                    await MySqlProtocol.SendOkPacketAsync(connection, cancellationToken: cancellationToken);
                    Console.WriteLine($"[MySQL] OK packet sent, waiting for client commands...");
                    Console.WriteLine($"[MySQL] User {handshakeResponse.Username} authenticated (connection {connection.ConnectionId})");

                    // Set database if specified
                    if (!string.IsNullOrEmpty(handshakeResponse.Database))
                    {
                        connection.SetDatabase(handshakeResponse.Database);
                    }

                    // For caching_sha2_password with empty auth response, client may send password in separate packet after OK
                    // But most clients will just send commands directly after OK
                    // Process commands - handle both password packet for caching_sha2_password and regular commands
                    try
                    {
                        // For caching_sha2_password with empty auth, client might send password packet or command
                        // Try to read next packet with a small timeout to see if it's a password or command
                        if (handshakeResponse.AuthPluginName == "caching_sha2_password" && 
                            (handshakeResponse.AuthResponse == null || handshakeResponse.AuthResponse.Length == 0))
                        {
                            Console.WriteLine($"[MySQL] Client requested caching_sha2_password with empty auth - waiting for password packet or command");
                            // Wait a bit to see if client sends password packet
                            // If not, proceed to command processing
                        }
                        
                        await ProcessCommandsAsync(connection, cancellationToken);
                    }
                    catch (IOException ex) when (ex.Message.Contains("Connection closed") || ex.Message.Contains("Broken pipe") || ex.Message.Contains("got 0 bytes"))
                    {
                        // Client closed connection - this is normal for interactive mode or when client just checks connection
                        Console.WriteLine($"[MySQL] Client closed connection (normal behavior): {ex.Message}");
                    }
                }
                else
                {
                    await MySqlProtocol.SendErrorPacketAsync(connection, 1045, $"Access denied for user '{handshakeResponse.Username}'@'localhost' (using password: YES)", cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySQL] Error reading handshake response: {ex.Message}");
                Console.WriteLine($"[MySQL] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[MySQL] Stack trace: {ex.StackTrace}");
                if (connection != null)
                {
                    try
                    {
                        await MySqlProtocol.SendErrorPacketAsync(connection, 2006, $"MySQL server has gone away: {ex.Message}", cancellationToken: cancellationToken);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MySQL] Client handling error: {ex.Message}");
            Console.WriteLine($"[MySQL] Stack trace: {ex.StackTrace}");
            if (connection != null)
            {
                try
                {
                    await MySqlProtocol.SendErrorPacketAsync(connection, 2006, $"MySQL server has gone away: {ex.Message}", cancellationToken: cancellationToken);
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[MySQL] Error sending error packet: {ex2.Message}");
                }
            }
        }
        finally
        {
            if (connection != null)
            {
                _connectionPool.RemoveConnection(connection.ConnectionId);
                _connectionDatabases.Remove(connection.ConnectionId);
                Console.WriteLine($"MySQL connection closed: {connection.ConnectionId}");
            }
        }
    }

    private async Task ProcessCommandsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && connection.IsConnected)
        {
            try
            {
                Console.WriteLine($"[MySQL] Waiting for command from client...");
                var command = await MySqlProtocol.ReadCommandAsync(connection, cancellationToken);
                Console.WriteLine($"[MySQL] Received command: {command.Type}");

                switch (command.Type)
                {
                    case MySqlCommandType.COM_QUIT:
                        Console.WriteLine($"[MySQL] Client requested QUIT");
                        return;

                    case MySqlCommandType.COM_INIT_DB:
                        await HandleInitDbAsync(connection, command.Text, cancellationToken);
                        break;

                    case MySqlCommandType.COM_QUERY:
                        await HandleQueryAsync(connection, command.Text, cancellationToken);
                        break;

                    case MySqlCommandType.COM_PING:
                        await MySqlProtocol.SendOkPacketAsync(connection, cancellationToken: cancellationToken);
                        break;

                    default:
                        Console.WriteLine($"[MySQL] Unsupported command type: {command.Type}");
                        await MySqlProtocol.SendErrorPacketAsync(connection, 1047, $"Command {command.Type} not supported", cancellationToken: cancellationToken);
                        break;
                }
            }
            catch (IOException ex) when (ex.Message.Contains("Connection closed") || ex.Message.Contains("Broken pipe") || ex.Message.Contains("got 0 bytes"))
            {
                // Client closed connection - this is normal, just exit
                Console.WriteLine($"[MySQL] Client closed connection normally: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySQL] Error processing command: {ex.Message}");
                Console.WriteLine($"[MySQL] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[MySQL] Stack trace: {ex.StackTrace}");
                try
                {
                    await MySqlProtocol.SendErrorPacketAsync(connection, 1064, $"Error: {ex.Message}", cancellationToken: cancellationToken);
                }
                catch
                {
                    // Connection may be closed, just exit
                    return;
                }
                break;
            }
        }
    }

    private async Task HandleInitDbAsync(MySqlConnection connection, string databaseName, CancellationToken cancellationToken)
    {
        var db = GetDatabase(databaseName);
        if (db == null)
        {
            await MySqlProtocol.SendErrorPacketAsync(connection, 1049, $"Unknown database '{databaseName}'", cancellationToken: cancellationToken);
            return;
        }

        connection.SetDatabase(databaseName);
        await MySqlProtocol.SendOkPacketAsync(connection, cancellationToken: cancellationToken);
    }

    private async Task HandleQueryAsync(MySqlConnection connection, string query, CancellationToken cancellationToken)
    {
        var database = connection.CurrentDatabase != null ? GetDatabase(connection.CurrentDatabase) : null;
        
        var handler = new MySqlQueryHandler(
            database,
            _modelService,
            _authService,
            _systemDatabaseService,
            _databases,
            GetDatabase);

        var result = await handler.HandleQueryAsync(query, connection, cancellationToken);

        if (!result.Success)
        {
            await MySqlProtocol.SendErrorPacketAsync(connection, 1064, result.ErrorMessage ?? "Query error", cancellationToken: cancellationToken);
            return;
        }

        // If it's a result set (SELECT)
        if (result.Columns != null && result.Rows != null)
        {
            // Send column count
            var columnCountPacket = new MemoryStream();
            MySqlPacketWriter.WriteLengthEncodedInteger(columnCountPacket, result.Columns.Length);
            await connection.WritePacketAsync(columnCountPacket.ToArray(), cancellationToken);

            // Send column definitions
            foreach (var column in result.Columns)
            {
                await MySqlProtocol.SendColumnDefinitionAsync(
                    connection,
                    "def", // catalog
                    connection.CurrentDatabase ?? "", // schema
                    "", // table
                    "", // org table
                    column.Name, // name
                    column.Name, // org name
                    33, // character set (utf8mb4)
                    column.Length, // column length
                    column.Type, // column type
                    column.Flags, // flags
                    column.Decimals, // decimals
                    cancellationToken);
            }

            // Send EOF after columns
            await MySqlProtocol.SendEofPacketAsync(connection, cancellationToken: cancellationToken);

            // Send rows
            foreach (var row in result.Rows)
            {
                await MySqlProtocol.SendRowAsync(connection, row, cancellationToken);
            }

            // Send EOF after rows
            await MySqlProtocol.SendEofPacketAsync(connection, cancellationToken: cancellationToken);
        }
        else
        {
            // Send OK packet for non-SELECT queries
            await MySqlProtocol.SendOkPacketAsync(
                connection,
                affectedRows: result.AffectedRows,
                lastInsertId: result.LastInsertId,
                info: result.Message,
                cancellationToken: cancellationToken);
        }
    }

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


using System.Buffers.Binary;
using System.Text;
using ovudb.Network.Authentication;

namespace ovudb.Network.MySQL;

/// <summary>
/// MySQL protocol implementation
/// </summary>
public static class MySqlProtocol
{
    private static readonly Encoding Encoding = Encoding.UTF8;
    private const byte ProtocolVersion = 10; // MySQL 5.7+ protocol version
    private const string ServerVersion = "5.7.0-OvuDB";
    private const int DefaultCapabilities = 0x00000200 | 0x00008000 | 0x00020000 | 0x00080000 | 0x00200000 | 0x00800000 | 0x01000000 | 0x02000000 | 0x04000000 | 0x08000000 | 0x10000000 | 0x20000000;
    // CLIENT_PROTOCOL_41 | CLIENT_SECURE_CONNECTION | CLIENT_PLUGIN_AUTH | CLIENT_CONNECT_ATTRS | CLIENT_PLUGIN_AUTH_LENENC_CLIENT_DATA | CLIENT_CAN_HANDLE_EXPIRED_PASSWORDS | CLIENT_SESSION_TRACK | CLIENT_DEPRECATE_EOF | CLIENT_OPTIONAL_RESULTSET_METADATA | CLIENT_ZSTD_COMPRESSION_ALGORITHM | CLIENT_QUERY_ATTRIBUTES | CLIENT_CAPABILITY_EXTENSION

    /// <summary>
    /// Send initial handshake packet
    /// </summary>
    public static async Task SendHandshakeAsync(MySqlConnection connection, CancellationToken cancellationToken = default)
    {
        var packet = new MemoryStream();
        
        // Protocol version
        packet.WriteByte(ProtocolVersion);
        
        // Server version (null-terminated)
        MySqlPacketWriter.WriteNullTerminatedString(packet, ServerVersion, Encoding);
        
        // Connection ID (4 bytes) - little endian, must be positive
        var connectionId = (uint)Math.Abs(connection.ConnectionId.GetHashCode());
        var connectionIdBytes = BitConverter.GetBytes(connectionId);
        packet.Write(connectionIdBytes, 0, 4);
        
        // Auth plugin data part 1 (8 bytes) - this is the salt
        var authData1 = new byte[8];
        Random.Shared.NextBytes(authData1);
        packet.Write(authData1, 0, 8);
        
        // Filler (1 byte) - must be 0
        packet.WriteByte(0);
        
        // Capabilities lower 2 bytes - little endian
        var capabilitiesLow = (ushort)(DefaultCapabilities & 0xFFFF);
        var capabilitiesLowBytes = BitConverter.GetBytes(capabilitiesLow);
        packet.Write(capabilitiesLowBytes, 0, 2);
        
        // Character set (1 byte) - utf8mb4
        packet.WriteByte(0xFF); // utf8mb4_general_ci
        
        // Status flags (2 bytes) - little endian
        packet.WriteByte(0x02); // SERVER_STATUS_AUTOCOMMIT
        packet.WriteByte(0x00);
        
        // Capabilities upper 2 bytes - little endian
        var capabilitiesHigh = (ushort)((DefaultCapabilities >> 16) & 0xFFFF);
        var capabilitiesHighBytes = BitConverter.GetBytes(capabilitiesHigh);
        packet.Write(capabilitiesHighBytes, 0, 2);
        
        // Auth plugin data len (1 byte) - length of auth plugin data
        // For both mysql_native_password and caching_sha2_password, we use 20 bytes
        packet.WriteByte(20); // 20 bytes total (8 + 12)
        
        // Reserved (10 bytes) - must be zeros
        packet.Write(new byte[10], 0, 10);
        
        // Auth plugin data part 2 (12 bytes to make total 20 with part 1)
        // Total auth plugin data should be 20 bytes (8 + 12)
        var authData2 = new byte[12];
        Random.Shared.NextBytes(authData2);
        packet.Write(authData2, 0, 12);
        
        // Auth plugin name (null-terminated) - mysql_native_password
        // Note: mysql_native_password is more compatible and simpler to implement
        // Modern clients can fall back to mysql_native_password if they request caching_sha2_password
        MySqlPacketWriter.WriteNullTerminatedString(packet, "mysql_native_password", Encoding);
        
        var handshakeData = packet.ToArray();
        Console.WriteLine($"[MySQL Protocol] Sending handshake packet: {handshakeData.Length} bytes");
        Console.WriteLine($"[MySQL Protocol] Full packet hex: {BitConverter.ToString(handshakeData)}");
        Console.WriteLine($"[MySQL Protocol] Packet structure:");
        Console.WriteLine($"[MySQL Protocol]   Protocol version: {handshakeData[0]}");
        var versionEnd = Array.IndexOf(handshakeData, (byte)0, 1);
        Console.WriteLine($"[MySQL Protocol]   Server version: {Encoding.GetString(handshakeData, 1, versionEnd - 1)}");
        var connIdOffset = versionEnd + 1;
        var connId = BitConverter.ToUInt32(handshakeData, connIdOffset);
        Console.WriteLine($"[MySQL Protocol]   Connection ID: {connId}");
        
        await connection.WritePacketAsync(handshakeData, cancellationToken);
        Console.WriteLine($"[MySQL Protocol] Handshake packet sent successfully");
    }

    /// <summary>
    /// Read handshake response
    /// </summary>
    public static async Task<HandshakeResponse> ReadHandshakeResponseAsync(MySqlConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            var packet = await connection.ReadPacketAsync(cancellationToken);
            Console.WriteLine($"[MySQL Protocol] Handshake response packet: {packet.Length} bytes");
            Console.WriteLine($"[MySQL Protocol] First 30 bytes: {BitConverter.ToString(packet, 0, Math.Min(30, packet.Length))}");
            
            var offset = 0;
        
        var capabilities = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(offset, 4));
        offset += 4;
        Console.WriteLine($"[MySQL Protocol] Client capabilities: 0x{capabilities:X8}");
        
        var maxPacketSize = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(offset, 4));
        offset += 4;
        Console.WriteLine($"[MySQL Protocol] Max packet size: {maxPacketSize}");
        
        var characterSet = packet[offset++];
        Console.WriteLine($"[MySQL Protocol] Character set: {characterSet}");
        
        // Skip reserved (23 bytes)
        offset += 23;
        
        // Read username (null-terminated)
        var username = MySqlPacketReader.ReadNullTerminatedString(packet, ref offset, Encoding);
        Console.WriteLine($"[MySQL Protocol] Username: {username}, offset after username: {offset}");
        
        // Read auth response
        byte[]? authResponse = null;
        if ((capabilities & 0x00080000) != 0) // CLIENT_PLUGIN_AUTH_LENENC_CLIENT_DATA
        {
            Console.WriteLine($"[MySQL Protocol] Using length-encoded auth response");
            var authResponseLength = MySqlPacketReader.ReadLengthEncodedInteger(packet, ref offset);
            Console.WriteLine($"[MySQL Protocol] Auth response length (lenenc): {authResponseLength}, offset: {offset}");
            if (authResponseLength > 0)
            {
                authResponse = new byte[authResponseLength];
                Array.Copy(packet, offset, authResponse, 0, authResponseLength);
                offset += authResponseLength;
                Console.WriteLine($"[MySQL Protocol] Auth response bytes: {BitConverter.ToString(authResponse)}");
            }
        }
        else
        {
            Console.WriteLine($"[MySQL Protocol] Using fixed-length auth response");
            var authResponseLength = packet[offset++];
            Console.WriteLine($"[MySQL Protocol] Auth response length (fixed): {authResponseLength}, offset: {offset}");
            if (authResponseLength > 0 && authResponseLength < 255) // 255 means empty password
            {
                authResponse = new byte[authResponseLength];
                Array.Copy(packet, offset, authResponse, 0, authResponseLength);
                offset += authResponseLength;
                Console.WriteLine($"[MySQL Protocol] Auth response bytes: {BitConverter.ToString(authResponse)}");
            }
        }
        
        // Read auth plugin name (if CLIENT_PLUGIN_AUTH is set)
        string? authPluginName = null;
        if ((capabilities & 0x00020000) != 0 && offset < packet.Length && packet[offset] != 0) // CLIENT_PLUGIN_AUTH
        {
            authPluginName = MySqlPacketReader.ReadNullTerminatedString(packet, ref offset, Encoding);
            Console.WriteLine($"[MySQL Protocol] Auth plugin name: {authPluginName}");
        }
        
        // Read database name (null-terminated, optional)
        string? database = null;
        if ((capabilities & 0x00000008) != 0 && offset < packet.Length && packet[offset] != 0) // CLIENT_CONNECT_WITH_DB
        {
            database = MySqlPacketReader.ReadNullTerminatedString(packet, ref offset, Encoding);
            Console.WriteLine($"[MySQL Protocol] Database: {database}");
        }
        
        Console.WriteLine($"[MySQL Protocol] Final offset: {offset}, packet length: {packet.Length}");
        
            return new HandshakeResponse
            {
                Capabilities = capabilities,
                MaxPacketSize = maxPacketSize,
                CharacterSet = characterSet,
                Username = username,
                AuthResponse = authResponse,
                AuthPluginName = authPluginName,
                Database = database
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MySQL Protocol] Error reading handshake response: {ex.Message}");
            Console.WriteLine($"[MySQL Protocol] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// Send OK packet
    /// </summary>
    public static async Task SendOkPacketAsync(MySqlConnection connection, int affectedRows = 0, long lastInsertId = 0, int statusFlags = 0x02, int warnings = 0, string? info = null, CancellationToken cancellationToken = default)
    {
        var packet = new MemoryStream();
        
        // Packet type: OK (0x00)
        packet.WriteByte(0x00);
        
        // Affected rows (length-encoded)
        MySqlPacketWriter.WriteLengthEncodedInteger(packet, affectedRows);
        
        // Last insert ID (length-encoded)
        MySqlPacketWriter.WriteLengthEncodedInteger(packet, lastInsertId);
        
        // Status flags (2 bytes) - little endian
        var statusBytes = BitConverter.GetBytes((ushort)statusFlags);
        packet.Write(statusBytes, 0, 2);
        
        // Warnings (2 bytes) - little endian
        var warningsBytes = BitConverter.GetBytes((ushort)warnings);
        packet.Write(warningsBytes, 0, 2);
        
        // Info message (optional, null-terminated)
        if (!string.IsNullOrEmpty(info))
        {
            MySqlPacketWriter.WriteNullTerminatedString(packet, info, Encoding);
        }
        
        var okData = packet.ToArray();
        Console.WriteLine($"[MySQL Protocol] Sending OK packet: {okData.Length} bytes");
        Console.WriteLine($"[MySQL Protocol] OK packet hex: {BitConverter.ToString(okData)}");
        Console.WriteLine($"[MySQL Protocol] OK packet: affectedRows={affectedRows}, lastInsertId={lastInsertId}, statusFlags=0x{statusFlags:X4}, warnings={warnings}");
        
        await connection.WritePacketAsync(okData, cancellationToken);
    }

    /// <summary>
    /// Send ERR packet
    /// </summary>
    public static async Task SendErrorPacketAsync(MySqlConnection connection, int errorCode, string errorMessage, string? sqlState = null, CancellationToken cancellationToken = default)
    {
        var packet = new MemoryStream();
        
        // Packet type: ERR (0xFF)
        packet.WriteByte(0xFF);
        
        // Error code (2 bytes)
        var errorCodeBytes = BitConverter.GetBytes((ushort)errorCode);
        packet.Write(errorCodeBytes, 0, 2);
        
        // SQL state marker ('#') and SQL state (5 bytes)
        if (!string.IsNullOrEmpty(sqlState))
        {
            packet.WriteByte(0x23); // '#'
            var sqlStateBytes = Encoding.GetBytes(sqlState);
            packet.Write(sqlStateBytes, 0, Math.Min(5, sqlStateBytes.Length));
        }
        else
        {
            packet.WriteByte(0x23); // '#'
            packet.Write(Encoding.GetBytes("HY000"), 0, 5);
        }
        
        // Error message (null-terminated)
        MySqlPacketWriter.WriteNullTerminatedString(packet, errorMessage, Encoding);
        
        await connection.WritePacketAsync(packet.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Send AUTH_SWITCH_REQUEST packet
    /// </summary>
    public static async Task SendAuthSwitchRequestAsync(MySqlConnection connection, string authPluginName, byte[] authPluginData, CancellationToken cancellationToken = default)
    {
        var packet = new MemoryStream();
        
        // Packet type: AUTH_SWITCH_REQUEST (0xFE)
        packet.WriteByte(0xFE);
        
        // Auth plugin name (null-terminated)
        MySqlPacketWriter.WriteNullTerminatedString(packet, authPluginName, Encoding);
        
        // Auth plugin data (null-terminated)
        packet.Write(authPluginData, 0, authPluginData.Length);
        packet.WriteByte(0); // Null terminator
        
        await connection.WritePacketAsync(packet.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Send EOF packet
    /// </summary>
    public static async Task SendEofPacketAsync(MySqlConnection connection, int warnings = 0, int statusFlags = 0x02, CancellationToken cancellationToken = default)
    {
        var packet = new MemoryStream();
        
        // Packet type: EOF (0xFE)
        packet.WriteByte(0xFE);
        
        // Warnings (2 bytes)
        var warningsBytes = BitConverter.GetBytes((ushort)warnings);
        packet.Write(warningsBytes, 0, 2);
        
        // Status flags (2 bytes)
        var statusBytes = BitConverter.GetBytes((ushort)statusFlags);
        packet.Write(statusBytes, 0, 2);
        
        await connection.WritePacketAsync(packet.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Send result set column definition
    /// </summary>
    public static async Task SendColumnDefinitionAsync(MySqlConnection connection, string catalog, string schema, string table, string orgTable, string name, string orgName, int characterSet, int columnLength, ColumnType columnType, int flags, int decimals, CancellationToken cancellationToken = default)
    {
        var packet = new MemoryStream();
        
        // Catalog (length-encoded string)
        MySqlPacketWriter.WriteLengthEncodedString(packet, catalog, Encoding);
        
        // Schema (length-encoded string)
        MySqlPacketWriter.WriteLengthEncodedString(packet, schema, Encoding);
        
        // Table (length-encoded string)
        MySqlPacketWriter.WriteLengthEncodedString(packet, table, Encoding);
        
        // Org table (length-encoded string)
        MySqlPacketWriter.WriteLengthEncodedString(packet, orgTable, Encoding);
        
        // Name (length-encoded string)
        MySqlPacketWriter.WriteLengthEncodedString(packet, name, Encoding);
        
        // Org name (length-encoded string)
        MySqlPacketWriter.WriteLengthEncodedString(packet, orgName, Encoding);
        
        // Length of fixed-length fields (1 byte)
        packet.WriteByte(0x0C);
        
        // Character set (2 bytes)
        var charsetBytes = BitConverter.GetBytes((ushort)characterSet);
        packet.Write(charsetBytes, 0, 2);
        
        // Column length (4 bytes)
        var lengthBytes = BitConverter.GetBytes(columnLength);
        packet.Write(lengthBytes, 0, 4);
        
        // Column type (1 byte)
        packet.WriteByte((byte)columnType);
        
        // Flags (2 bytes)
        var flagsBytes = BitConverter.GetBytes((ushort)flags);
        packet.Write(flagsBytes, 0, 2);
        
        // Decimals (1 byte)
        packet.WriteByte((byte)decimals);
        
        // Filler (2 bytes)
        packet.WriteByte(0x00);
        packet.WriteByte(0x00);
        
        await connection.WritePacketAsync(packet.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Send result set row
    /// </summary>
    public static async Task SendRowAsync(MySqlConnection connection, object?[] values, CancellationToken cancellationToken = default)
    {
        var packet = new MemoryStream();
        
        foreach (var value in values)
        {
            if (value == null || value == DBNull.Value)
            {
                // NULL value
                packet.WriteByte(0xFB);
            }
            else
            {
                // Convert value to string and send as length-encoded string
                var strValue = value.ToString() ?? string.Empty;
                MySqlPacketWriter.WriteLengthEncodedString(packet, strValue, Encoding);
            }
        }
        
        await connection.WritePacketAsync(packet.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Read command packet
    /// </summary>
    public static async Task<MySqlCommand> ReadCommandAsync(MySqlConnection connection, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[MySQL Protocol] Reading command packet...");
        var packet = await connection.ReadPacketAsync(cancellationToken);
        Console.WriteLine($"[MySQL Protocol] Command packet received: {packet.Length} bytes");
        Console.WriteLine($"[MySQL Protocol] First 20 bytes: {BitConverter.ToString(packet, 0, Math.Min(20, packet.Length))}");
        
        if (packet.Length == 0)
        {
            throw new IOException("Empty command packet");
        }
        
        var commandType = (MySqlCommandType)packet[0];
        var commandText = string.Empty;
        
        if (packet.Length > 1)
        {
            commandText = Encoding.GetString(packet, 1, packet.Length - 1);
        }
        
        Console.WriteLine($"[MySQL Protocol] Command type: {commandType} (0x{packet[0]:X2}), text length: {commandText.Length}");
        
        return new MySqlCommand
        {
            Type = commandType,
            Text = commandText
        };
    }
}

/// <summary>
/// Handshake response from client
/// </summary>
public class HandshakeResponse
{
    public uint Capabilities { get; set; }
    public uint MaxPacketSize { get; set; }
    public byte CharacterSet { get; set; }
    public string Username { get; set; } = string.Empty;
    public byte[]? AuthResponse { get; set; }
    public string? AuthPluginName { get; set; }
    public string? Database { get; set; }
}

/// <summary>
/// MySQL command
/// </summary>
public class MySqlCommand
{
    public MySqlCommandType Type { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// MySQL command types
/// </summary>
public enum MySqlCommandType : byte
{
    COM_SLEEP = 0x00,
    COM_QUIT = 0x01,
    COM_INIT_DB = 0x02,
    COM_QUERY = 0x03,
    COM_FIELD_LIST = 0x04,
    COM_CREATE_DB = 0x05,
    COM_DROP_DB = 0x06,
    COM_REFRESH = 0x07,
    COM_SHUTDOWN = 0x08,
    COM_STATISTICS = 0x09,
    COM_PROCESS_INFO = 0x0A,
    COM_CONNECT = 0x0B,
    COM_PROCESS_KILL = 0x0C,
    COM_DEBUG = 0x0D,
    COM_PING = 0x0E,
    COM_TIME = 0x0F,
    COM_DELAYED_INSERT = 0x10,
    COM_CHANGE_USER = 0x11,
    COM_BINLOG_DUMP = 0x12,
    COM_TABLE_DUMP = 0x13,
    COM_CONNECT_OUT = 0x14,
    COM_REGISTER_SLAVE = 0x15,
    COM_STMT_PREPARE = 0x16,
    COM_STMT_EXECUTE = 0x17,
    COM_STMT_SEND_LONG_DATA = 0x18,
    COM_STMT_CLOSE = 0x19,
    COM_STMT_RESET = 0x1A,
    COM_SET_OPTION = 0x1B,
    COM_STMT_FETCH = 0x1C,
    COM_DAEMON = 0x1D,
    COM_BINLOG_DUMP_GTID = 0x1E,
    COM_RESET_CONNECTION = 0x1F
}

/// <summary>
/// MySQL column types
/// </summary>
public enum ColumnType : byte
{
    MYSQL_TYPE_DECIMAL = 0x00,
    MYSQL_TYPE_TINY = 0x01,
    MYSQL_TYPE_SHORT = 0x02,
    MYSQL_TYPE_LONG = 0x03,
    MYSQL_TYPE_FLOAT = 0x04,
    MYSQL_TYPE_DOUBLE = 0x05,
    MYSQL_TYPE_NULL = 0x06,
    MYSQL_TYPE_TIMESTAMP = 0x07,
    MYSQL_TYPE_LONGLONG = 0x08,
    MYSQL_TYPE_INT24 = 0x09,
    MYSQL_TYPE_DATE = 0x0A,
    MYSQL_TYPE_TIME = 0x0B,
    MYSQL_TYPE_DATETIME = 0x0C,
    MYSQL_TYPE_YEAR = 0x0D,
    MYSQL_TYPE_NEWDATE = 0x0E,
    MYSQL_TYPE_VARCHAR = 0x0F,
    MYSQL_TYPE_BIT = 0x10,
    MYSQL_TYPE_TIMESTAMP2 = 0x11,
    MYSQL_TYPE_DATETIME2 = 0x12,
    MYSQL_TYPE_TIME2 = 0x13,
    MYSQL_TYPE_JSON = 0xF5,
    MYSQL_TYPE_NEWDECIMAL = 0xF6,
    MYSQL_TYPE_ENUM = 0xF7,
    MYSQL_TYPE_SET = 0xF8,
    MYSQL_TYPE_TINY_BLOB = 0xF9,
    MYSQL_TYPE_MEDIUM_BLOB = 0xFA,
    MYSQL_TYPE_LONG_BLOB = 0xFB,
    MYSQL_TYPE_BLOB = 0xFC,
    MYSQL_TYPE_VAR_STRING = 0xFD,
    MYSQL_TYPE_STRING = 0xFE,
    MYSQL_TYPE_GEOMETRY = 0xFF
}


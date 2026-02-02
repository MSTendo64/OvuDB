using System.Buffers.Binary;
using System.Text;

namespace ovudb.Network.MySQL;

/// <summary>
/// Reader for MySQL protocol packets
/// </summary>
public class MySqlPacketReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[16 * 1024 * 1024]; // 16MB max packet size

    public MySqlPacketReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Read a MySQL packet
    /// </summary>
    public async Task<byte[]> ReadPacketAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Console.WriteLine($"[MySQL PacketReader] Starting to read packet header...");
            // Read packet header (3 bytes length + 1 byte sequence)
            var header = new byte[4];
            
            // Don't use timeout for handshake response - client should respond immediately
            // Use timeout only for regular commands
            var bytesRead = await _stream.ReadAsync(header, 0, 4, cancellationToken);
            Console.WriteLine($"[MySQL PacketReader] Read {bytesRead} bytes for header");
            
            if (bytesRead != 4)
            {
                if (bytesRead == 0)
                {
                    Console.WriteLine($"[MySQL PacketReader] Connection closed by client (0 bytes read)");
                }
                else
                {
                    Console.WriteLine($"[MySQL PacketReader] Incomplete header: got {bytesRead} bytes instead of 4");
                    Console.WriteLine($"[MySQL PacketReader] Header bytes: {BitConverter.ToString(header, 0, bytesRead)}");
                }
                throw new IOException($"Failed to read packet header: got {bytesRead} bytes instead of 4");
            }

            var packetLength = header[0] | (header[1] << 8) | (header[2] << 16);
            var sequenceId = header[3];
            Console.WriteLine($"[MySQL PacketReader] Packet header: length={packetLength}, sequence={sequenceId}");
            Console.WriteLine($"[MySQL PacketReader] Header bytes: {BitConverter.ToString(header)}");
            
            // Sanity check: if packet length is unreasonably large, it's likely not a valid MySQL packet
            // MySQL max packet size is typically 16MB (16777216 bytes), but we'll be more conservative
            if (packetLength > 16 * 1024 * 1024) // 16MB
            {
                Console.WriteLine($"[MySQL PacketReader] WARNING: Packet length {packetLength} is unreasonably large, likely invalid packet format");
                Console.WriteLine($"[MySQL PacketReader] This might indicate the client is sending data in wrong format");
                Console.WriteLine($"[MySQL PacketReader] Header bytes were: {BitConverter.ToString(header)}");
                Console.WriteLine($"[MySQL PacketReader] Header as ASCII: {System.Text.Encoding.ASCII.GetString(header)}");
                
                // Try to read a small amount to see what we actually got
                var peekBuffer = new byte[Math.Min(100, 100)]; // Read max 100 bytes for debugging
                var peekRead = await _stream.ReadAsync(peekBuffer, 0, peekBuffer.Length, cancellationToken);
                Console.WriteLine($"[MySQL PacketReader] First {peekRead} bytes of data: {BitConverter.ToString(peekBuffer, 0, peekRead)}");
                Console.WriteLine($"[MySQL PacketReader] As ASCII: {System.Text.Encoding.ASCII.GetString(peekBuffer, 0, peekRead)}");
                Console.WriteLine($"[MySQL PacketReader] ERROR: Client is sending data in wrong format - not a MySQL packet!");
                Console.WriteLine($"[MySQL PacketReader] This usually means the client didn't receive the handshake properly or is using wrong protocol");
                throw new IOException($"Invalid packet format: packet length {packetLength} is too large (likely not a MySQL packet). Client may not have received handshake properly.");
            }

            if (packetLength > _buffer.Length)
            {
                Console.WriteLine($"[MySQL PacketReader] Packet too large: {packetLength} bytes (max: {_buffer.Length})");
                throw new IOException($"Packet too large: {packetLength} bytes");
            }

            if (packetLength == 0)
            {
                Console.WriteLine($"[MySQL PacketReader] Empty packet received");
                return new byte[0];
            }

            // Read packet payload
            Console.WriteLine($"[MySQL PacketReader] Reading packet payload ({packetLength} bytes)...");
            var totalRead = 0;
            while (totalRead < packetLength)
            {
                var read = await _stream.ReadAsync(_buffer, totalRead, packetLength - totalRead, cancellationToken);
                Console.WriteLine($"[MySQL PacketReader] Read {read} bytes of payload (total: {totalRead + read}/{packetLength})");
                if (read == 0)
                {
                    Console.WriteLine($"[MySQL PacketReader] Connection closed while reading payload");
                    throw new IOException("Unexpected end of stream");
                }
                totalRead += read;
            }

            var packet = new byte[packetLength];
            Array.Copy(_buffer, 0, packet, 0, packetLength);
            Console.WriteLine($"[MySQL PacketReader] ✓ Successfully read packet: length={packetLength}, sequence={sequenceId}");
            Console.WriteLine($"[MySQL PacketReader] First 20 bytes: {BitConverter.ToString(packet, 0, Math.Min(20, packetLength))}");
            return packet;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MySQL PacketReader] ✗ Error reading packet: {ex.Message}");
            Console.WriteLine($"[MySQL PacketReader] Exception type: {ex.GetType().Name}");
            Console.WriteLine($"[MySQL PacketReader] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// Read length-encoded integer
    /// </summary>
    public static int ReadLengthEncodedInteger(byte[] data, ref int offset)
    {
        if (offset >= data.Length)
            throw new IndexOutOfRangeException();

        var firstByte = data[offset++];
        
        if (firstByte < 251)
        {
            return firstByte;
        }
        else if (firstByte == 0xFC)
        {
            if (offset + 2 > data.Length)
                throw new IndexOutOfRangeException();
            var value = BitConverter.ToUInt16(data, offset);
            offset += 2;
            return value;
        }
        else if (firstByte == 0xFD)
        {
            if (offset + 3 > data.Length)
                throw new IndexOutOfRangeException();
            var value = BitConverter.ToUInt32(data, offset);
            offset += 3;
            return (int)value;
        }
        else if (firstByte == 0xFE)
        {
            if (offset + 8 > data.Length)
                throw new IndexOutOfRangeException();
            var value = BitConverter.ToUInt64(data, offset);
            offset += 8;
            return (int)value;
        }
        else
        {
            throw new NotSupportedException($"Unsupported length-encoded integer prefix: 0x{firstByte:X2}");
        }
    }

    /// <summary>
    /// Read length-encoded string
    /// </summary>
    public static string ReadLengthEncodedString(byte[] data, ref int offset, Encoding encoding)
    {
        var length = ReadLengthEncodedInteger(data, ref offset);
        if (length == 0)
            return string.Empty;

        if (offset + length > data.Length)
            throw new IndexOutOfRangeException();

        var str = encoding.GetString(data, offset, length);
        offset += length;
        return str;
    }

    /// <summary>
    /// Read null-terminated string
    /// </summary>
    public static string ReadNullTerminatedString(byte[] data, ref int offset, Encoding encoding)
    {
        var start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }

        if (offset >= data.Length)
            throw new IndexOutOfRangeException();

        var length = offset - start;
        var str = length > 0 ? encoding.GetString(data, start, length) : string.Empty;
        offset++; // Skip null terminator
        return str;
    }

    /// <summary>
    /// Read fixed-length string
    /// </summary>
    public static string ReadFixedString(byte[] data, ref int offset, int length, Encoding encoding)
    {
        if (offset + length > data.Length)
            throw new IndexOutOfRangeException();

        var str = encoding.GetString(data, offset, length);
        offset += length;
        return str;
    }
}


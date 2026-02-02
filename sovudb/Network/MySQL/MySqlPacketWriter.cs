using System.Buffers.Binary;
using System.Text;

namespace ovudb.Network.MySQL;

/// <summary>
/// Writer for MySQL protocol packets
/// </summary>
public class MySqlPacketWriter
{
    private readonly Stream _stream;
    private readonly MemoryStream _buffer = new();
    private byte _sequenceId = 0;

    public MySqlPacketWriter(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Write a MySQL packet
    /// </summary>
    public async Task WritePacketAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var length = data.Length;
        
        // Write packet header: 3 bytes length + 1 byte sequence
        var header = new byte[4];
        header[0] = (byte)(length & 0xFF);
        header[1] = (byte)((length >> 8) & 0xFF);
        header[2] = (byte)((length >> 16) & 0xFF);
        header[3] = _sequenceId++;

        Console.WriteLine($"[MySQL PacketWriter] Writing packet: length={length}, sequence={header[3]}");
        Console.WriteLine($"[MySQL PacketWriter] Header: {BitConverter.ToString(header)}");
        
        await _stream.WriteAsync(header, 0, 4, cancellationToken);
        await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
        
        Console.WriteLine($"[MySQL PacketWriter] Packet written and flushed");
    }

    /// <summary>
    /// Write length-encoded integer
    /// </summary>
    public static void WriteLengthEncodedInteger(MemoryStream stream, long value)
    {
        if (value < 251)
        {
            stream.WriteByte((byte)value);
        }
        else if (value < 65536)
        {
            stream.WriteByte(0xFC);
            var bytes = BitConverter.GetBytes((ushort)value);
            stream.Write(bytes, 0, 2);
        }
        else if (value < 16777216)
        {
            stream.WriteByte(0xFD);
            var bytes = BitConverter.GetBytes((uint)value);
            stream.Write(bytes, 0, 3);
        }
        else
        {
            stream.WriteByte(0xFE);
            var bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, 8);
        }
    }

    /// <summary>
    /// Write length-encoded string
    /// </summary>
    public static void WriteLengthEncodedString(MemoryStream stream, string value, Encoding encoding)
    {
        if (string.IsNullOrEmpty(value))
        {
            stream.WriteByte(0);
            return;
        }

        var bytes = encoding.GetBytes(value);
        WriteLengthEncodedInteger(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Write null-terminated string
    /// </summary>
    public static void WriteNullTerminatedString(MemoryStream stream, string value, Encoding encoding)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var bytes = encoding.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
        stream.WriteByte(0); // Null terminator
    }

    /// <summary>
    /// Write fixed-length string (padded with zeros if needed)
    /// </summary>
    public static void WriteFixedString(MemoryStream stream, string value, int length, Encoding encoding)
    {
        var bytes = encoding.GetBytes(value ?? string.Empty);
        var bytesToWrite = Math.Min(bytes.Length, length);
        stream.Write(bytes, 0, bytesToWrite);
        
        // Pad with zeros if needed
        for (int i = bytesToWrite; i < length; i++)
        {
            stream.WriteByte(0);
        }
    }

    /// <summary>
    /// Reset sequence ID (for new connection)
    /// </summary>
    public void ResetSequence()
    {
        _sequenceId = 0;
    }
}


using System.Text;

namespace ovudb.Storage;

/// <summary>
/// Binary reader for OvuDB (similar to PostgreSQL)
/// </summary>
internal class BinaryReader : IDisposable
{
    private readonly System.IO.BinaryReader _reader;
    private readonly Stream _stream;

    public BinaryReader(Stream stream)
    {
        _stream = stream;
        _reader = new System.IO.BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
    }

    public int ReadInt32() => _reader.ReadInt32();
    public long ReadInt64() => _reader.ReadInt64();
    public string? ReadString()
    {
        var length = _reader.ReadInt32();
        if (length == -1) return null;
        var bytes = _reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }
    public bool ReadBool() => _reader.ReadBoolean();
    public double ReadDouble() => _reader.ReadDouble();
    public DateTime ReadDateTime() => DateTime.FromBinary(_reader.ReadInt64());
    public byte[]? ReadBytes()
    {
        var length = _reader.ReadInt32();
        if (length == -1) return null;
        return _reader.ReadBytes(length);
    }
    public object? ReadObject()
    {
        var typeMarker = _reader.ReadByte();
        if (typeMarker == 0) return null; // NULL

        return typeMarker switch
        {
            1 => _reader.ReadInt32(), // int
            2 => _reader.ReadInt64(), // long
            3 => _reader.ReadDouble(), // double
            4 => ReadString(), // string
            5 => _reader.ReadBoolean(), // bool
            6 => DateTime.FromBinary(_reader.ReadInt64()), // DateTime
            7 => ReadBytes(), // byte[]
            _ => ReadString() // Default as string
        };
    }

    public long Position => _stream.Position;
    public long Length => _stream.Length;

    public void Dispose()
    {
        _reader?.Dispose();
        _stream?.Dispose();
    }
}

using System.Text;

namespace ovudb.Storage;

/// <summary>
/// Binary writer for OvuDB (similar to PostgreSQL)
/// </summary>
internal class BinaryWriter : IDisposable
{
    private readonly System.IO.BinaryWriter _writer;
    private readonly Stream _stream;

    public BinaryWriter(Stream stream)
    {
        _stream = stream;
        _writer = new System.IO.BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
    }

    public void WriteInt32(int value) => _writer.Write(value);
    public void WriteInt64(long value) => _writer.Write(value);
    public void WriteString(string value)
    {
        if (value == null)
        {
            _writer.Write(-1);
            return;
        }
        // Optimization: use Span for small strings (stackalloc up to 1KB)
        if (value.Length <= 256)
        {
            // For strings up to 256 chars use stackalloc (max ~768 bytes for UTF-8)
            Span<byte> buffer = stackalloc byte[768];
            var byteCount = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
            _writer.Write(byteCount);
            _writer.Write(buffer.Slice(0, byteCount));
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            _writer.Write(bytes.Length);
            _writer.Write(bytes);
        }
    }
    public void WriteBool(bool value) => _writer.Write(value);
    public void WriteDouble(double value) => _writer.Write(value);
    public void WriteDateTime(DateTime value) => _writer.Write(value.ToBinary());
    public void WriteBytes(byte[] value)
    {
        if (value == null)
        {
            _writer.Write(-1);
            return;
        }
        _writer.Write(value.Length);
        _writer.Write(value);
    }
    public void WriteObject(object? value)
    {
        if (value == null)
        {
            _writer.Write((byte)0); // NULL marker
            return;
        }

        var type = value.GetType();
        if (type == typeof(int))
        {
            _writer.Write((byte)1); // Type marker: int
            _writer.Write((int)value);
        }
        else if (type == typeof(long))
        {
            _writer.Write((byte)2); // Type marker: long
            _writer.Write((long)value);
        }
        else if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            _writer.Write((byte)3); // Type marker: double
            _writer.Write(Convert.ToDouble(value));
        }
        else if (type == typeof(string))
        {
            _writer.Write((byte)4); // Type marker: string
            WriteString((string)value);
        }
        else if (type == typeof(bool))
        {
            _writer.Write((byte)5); // Type marker: bool
            _writer.Write((bool)value);
        }
        else if (type == typeof(DateTime))
        {
            _writer.Write((byte)6); // Type marker: DateTime
            _writer.Write(((DateTime)value).ToBinary());
        }
        else if (type == typeof(byte[]))
        {
            _writer.Write((byte)7); // Type marker: byte[]
            WriteBytes((byte[])value);
        }
        else
        {
            // Default as string
            _writer.Write((byte)4);
            WriteString(value.ToString() ?? string.Empty);
        }
    }

    public void Flush() => _writer.Flush();
    public long Position => _stream.Position;

    public void Dispose()
    {
        _writer?.Dispose();
        _stream?.Dispose();
    }
}

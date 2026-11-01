using System.Buffers.Binary;
using System.Text;

namespace Vela.Api.Abuse;

/// <summary>
/// The sliver of CBOR an App Attest attestation object uses: definite-length maps with text keys, arrays, byte and text
/// strings, and integers. Anything else, or any malformed or oversized input, is a FormatException.
/// </summary>
public static class MiniCbor
{
    private const int MaxDepth = 8;
    private const int MaxItems = 64;

    public static object? Parse(byte[] data)
    {
        var pos = 0;
        var value = Read(data, ref pos, 0);
        return pos == data.Length ? value : throw new FormatException("trailing bytes after CBOR item");
    }

    private static object? Read(byte[] d, ref int pos, int depth)
    {
        if (depth > MaxDepth || pos >= d.Length) throw new FormatException("CBOR too deep or truncated");
        var initial = d[pos++];
        var major = initial >> 5;
        var length = ReadLength(d, ref pos, initial & 0x1F);
        switch (major)
        {
            case 0: return length;
            case 2: return Take(d, ref pos, length).ToArray();
            case 3: return Encoding.UTF8.GetString(Take(d, ref pos, length));
            case 4:
                if (length > MaxItems) throw new FormatException("CBOR array too long");
                var list = new List<object?>();
                for (var i = 0; i < length; i++) list.Add(Read(d, ref pos, depth + 1));
                return list;
            case 5:
                if (length > MaxItems) throw new FormatException("CBOR map too long");
                var map = new Dictionary<string, object?>();
                for (var i = 0; i < length; i++)
                {
                    var key = Read(d, ref pos, depth + 1) as string ?? throw new FormatException("CBOR map key is not text");
                    map[key] = Read(d, ref pos, depth + 1);
                }
                return map;
            default: throw new FormatException("unsupported CBOR type");
        }
    }

    private static long ReadLength(byte[] d, ref int pos, int info)
    {
        if (info < 24) return info;
        var size = info switch { 24 => 1, 25 => 2, 26 => 4, 27 => 8, _ => throw new FormatException("indefinite or reserved CBOR length") };
        var bytes = Take(d, ref pos, size);
        var value = size switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            _ => (long)BinaryPrimitives.ReadUInt64BigEndian(bytes),
        };
        return value < 0 ? throw new FormatException("CBOR length out of range") : value;
    }

    private static ReadOnlySpan<byte> Take(byte[] d, ref int pos, long count)
    {
        if (count < 0 || count > d.Length - pos) throw new FormatException("CBOR item exceeds input");
        var slice = d.AsSpan(pos, (int)count);
        pos += (int)count;
        return slice;
    }
}

using System.Buffers.Binary;

namespace M2Forge.Core.Psb;

public sealed class PsbStreamEditor
{
    const int HeaderSize = 56;

    readonly byte[] data;
    readonly List<PsbStreamLocation> streams = [];

    public PsbStreamEditor(byte[] psb)
    {
        data = (byte[])psb.Clone();

        if (data.Length < 32 || !data.AsSpan(0, 4).SequenceEqual("PSB\0"u8))
            throw new InvalidDataException("Not a PSB file.");

        Version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6));
        if (Version is < 1 or > 4)
            throw new NotSupportedException($"PSB version {Version} is not supported.");
        if (flags != 0)
            throw new NotSupportedException("Filtered PSB files are not supported.");

        ReadStreams(PsbStreamKind.Stream, Field(24), Field(28), Field(32));
        if (Version >= 4)
        {
            if (data.Length < HeaderSize) throw new InvalidDataException("Truncated PSB header.");
            ReadStreams(PsbStreamKind.BStream, Field(44), Field(48), Field(52));
        }
    }

    public ushort Version { get; }

    public IReadOnlyList<PsbStreamLocation> Streams => streams;

    public static PsbStreamEditor Load(string path) => new(File.ReadAllBytes(path));

    public ReadOnlyMemory<byte> Read(PsbStreamKind kind, uint index)
    {
        var stream = Find(kind, index);
        return data.AsMemory(stream.Offset, stream.Length);
    }

    public void Replace(PsbStreamKind kind, uint index, ReadOnlySpan<byte> replacement)
    {
        var stream = Find(kind, index);
        if (replacement.Length != stream.Length)
            throw new ArgumentException(
                $"Stream {index} holds {stream.Length} bytes, the replacement has {replacement.Length}.",
                nameof(replacement));
        replacement.CopyTo(data.AsSpan(stream.Offset, stream.Length));
    }

    public byte[] ToArray() => (byte[])data.Clone();

    public void Save(string path) => File.WriteAllBytes(path, data);

    PsbStreamLocation Find(PsbStreamKind kind, uint index) =>
        streams.FirstOrDefault(s => s.Kind == kind && s.Index == index)
        ?? throw new KeyNotFoundException($"No {kind} with index {index}.");

    int Field(int offset) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)));

    void ReadStreams(PsbStreamKind kind, int offsetsAt, int sizesAt, int entityAt)
    {
        var offsets = ReadUIntArray(offsetsAt);
        var sizes = ReadUIntArray(sizesAt);
        if (offsets.Length != sizes.Length)
            throw new InvalidDataException("Stream offset and size tables differ in length.");

        for (var i = 0; i < offsets.Length; i++)
        {
            var start = (long)entityAt + offsets[i];
            if (start + sizes[i] > data.Length)
                throw new InvalidDataException($"{kind} {i} lies outside the file.");
            streams.Add(new PsbStreamLocation(kind, (uint)i, (int)start, (int)sizes[i]));
        }
    }

    uint[] ReadUIntArray(int position)
    {
        var count = ReadUIntToken(ref position);
        var width = ReadTokenType(ref position);
        if (position + (long)count * width > data.Length)
            throw new InvalidDataException("Truncated PSB array.");

        var values = new uint[count];
        for (var i = 0; i < count; i++)
        {
            uint value = 0;
            for (var b = 0; b < width; b++)
                value |= (uint)data[position + b] << (8 * b);
            values[i] = value;
            position += width;
        }
        return values;
    }

    uint ReadUIntToken(ref int position)
    {
        var width = ReadTokenType(ref position);
        uint value = 0;
        for (var b = 0; b < width; b++)
            value |= (uint)data[position + b] << (8 * b);
        position += width;
        return value;
    }

    int ReadTokenType(ref int position)
    {
        if (position >= data.Length) throw new InvalidDataException("Truncated PSB array.");
        var type = data[position++];
        if (type is < 13 or > 16)
            throw new InvalidDataException($"Unexpected PSB token {type} in an unsigned integer array.");
        return type - 12;
    }
}

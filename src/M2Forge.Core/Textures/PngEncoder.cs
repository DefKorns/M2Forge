using System.Buffers.Binary;
using System.IO.Compression;

namespace M2Forge.Core.Textures;

public static class PngEncoder
{
    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(RgbaImage image, Stream output)
    {
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var stride = image.Width * 4;
            for (var y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(image.Pixels, y * stride, stride);
            }
        }
        WriteChunk(output, "IDAT"u8, raw.GetBuffer().AsSpan(0, (int)raw.Length));
        WriteChunk(output, "IEND"u8, []);
    }

    public static void Save(RgbaImage image, string path)
    {
        using var file = File.Create(path);
        Write(image, file);
    }

    static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(type);
        output.Write(data);

        var crc = 0xFFFFFFFFu;
        crc = Update(crc, type);
        crc = Update(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc);
        output.Write(word);
    }

    static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}

using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace M2Forge.Tests.Textures;

public static class PngBuilder
{
    public static byte[] Build(
        int width,
        int height,
        int bitDepth,
        int colorType,
        byte[][] rows,
        int[]? filters = null,
        byte[]? palette = null,
        byte[]? transparency = null,
        int interlace = 0)
    {
        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var bytesPerPixel = Math.Max(1, channels * bitDepth / 8);

        using var raw = new MemoryStream();
        byte[] previous = new byte[rows[0].Length];
        for (var y = 0; y < rows.Length; y++)
        {
            var filter = filters?[y % filters.Length] ?? 0;
            raw.WriteByte((byte)filter);
            raw.Write(Filter(rows[y], previous, filter, bytesPerPixel));
            previous = rows[y];
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw.ToArray());

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)bitDepth;
        header[9] = (byte)colorType;
        header[12] = (byte)interlace;
        Chunk(png, "IHDR", header);
        if (palette is not null) Chunk(png, "PLTE", palette);
        if (transparency is not null) Chunk(png, "tRNS", transparency);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static byte[] Filter(byte[] row, byte[] previous, int filter, int bpp)
    {
        var result = new byte[row.Length];
        for (var x = 0; x < row.Length; x++)
        {
            var left = x >= bpp ? row[x - bpp] : 0;
            var up = previous[x];
            var upLeft = x >= bpp ? previous[x - bpp] : 0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                _ => Paeth(left, up, upLeft),
            };
            result[x] = (byte)(row[x] - predictor);
        }
        return result;
    }

    static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    public static void Chunk(Stream output, string type, byte[] data)
    {
        var word = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = new Crc32();
        crc.Append(typeBytes);
        crc.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc.GetCurrentHashAsUInt32());
        output.Write(word);
    }
}

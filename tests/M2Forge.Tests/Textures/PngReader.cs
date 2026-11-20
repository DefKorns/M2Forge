using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace M2Forge.Tests.Textures;

public static class PngReader
{
    public static (int Width, int Height, byte[] Rgba) Read(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);

        int width = 0, height = 0;
        using var idat = new MemoryStream();
        var offset = 8;
        var ended = false;
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = png.AsSpan(offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));

            var crc = new Crc32();
            crc.Append(type);
            crc.Append(data);
            Assert.Equal(expectedCrc, crc.GetCurrentHashAsUInt32());

            switch (System.Text.Encoding.ASCII.GetString(type))
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    Assert.Equal(8, data[8]);
                    Assert.Equal(6, data[9]);
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    ended = true;
                    break;
            }
            offset += 12 + length;
        }
        Assert.True(ended);

        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress)) zlib.CopyTo(inflated);
        var raw = inflated.ToArray();

        var stride = width * 4;
        Assert.Equal((stride + 1) * height, raw.Length);

        var rgba = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            Assert.Equal(0, filter);
            Array.Copy(raw, y * (stride + 1) + 1, rgba, y * stride, stride);
        }
        return (width, height, rgba);
    }
}

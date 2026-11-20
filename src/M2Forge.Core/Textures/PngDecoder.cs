using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace M2Forge.Core.Textures;

public static class PngDecoder
{
    const int MaxPixels = 64 * 1024 * 1024;
    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static RgbaImage Decode(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file.");

        Header? header = null;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();

        var offset = 8;
        var ended = false;
        while (!ended)
        {
            if (offset + 12 > png.Length) throw new InvalidDataException("Truncated PNG file.");

            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            if (length > int.MaxValue || offset + 12L + length > png.Length)
                throw new InvalidDataException("Truncated PNG file.");

            var type = png.AsSpan(offset + 4, 4);
            var data = png.AsSpan(offset + 8, (int)length);

            var crc = new Crc32();
            crc.Append(type);
            crc.Append(data);
            if (crc.GetCurrentHashAsUInt32() != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + (int)length)))
                throw new InvalidDataException("PNG chunk checksum mismatch.");

            switch (System.Text.Encoding.ASCII.GetString(type))
            {
                case "IHDR":
                    header = ReadHeader(data);
                    break;
                case "PLTE":
                    palette = data.ToArray();
                    break;
                case "tRNS":
                    transparency = data.ToArray();
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    ended = true;
                    break;
            }
            offset += 12 + (int)length;
        }

        if (header is not { } h) throw new InvalidDataException("PNG has no header.");
        if (h.ColorType == 3 && palette is null) throw new InvalidDataException("Indexed PNG has no palette.");

        var raw = Inflate(compressed, h);
        Unfilter(raw, h);
        return ToRgba(raw, h, palette, transparency);
    }

    readonly record struct Header(int Width, int Height, int BitDepth, int ColorType)
    {
        public int Channels => ColorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        public int BitsPerPixel => Channels * BitDepth;
        public int BytesPerPixel => Math.Max(1, BitsPerPixel / 8);
        public int Stride => (Width * BitsPerPixel + 7) / 8;
    }

    static Header ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length != 13) throw new InvalidDataException("Invalid PNG header.");

        var width = BinaryPrimitives.ReadInt32BigEndian(data);
        var height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
        var depth = data[8];
        var colorType = data[9];

        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            throw new InvalidDataException($"PNG size {width}x{height} is not supported.");
        if (data[10] != 0 || data[11] != 0)
            throw new InvalidDataException("Unknown PNG compression or filter method.");
        if (data[12] != 0)
            throw new NotSupportedException("Interlaced PNG files are not supported; save the image without interlacing.");

        var valid = colorType switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => depth is 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => false,
        };
        if (!valid) throw new NotSupportedException($"PNG colour type {colorType} with {depth} bits is not supported.");

        return new Header(width, height, depth, colorType);
    }

    static byte[] Inflate(MemoryStream compressed, Header h)
    {
        var expected = (h.Stride + 1) * h.Height;
        var raw = new byte[expected];

        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var read = 0;
        while (read < expected)
        {
            var n = zlib.Read(raw, read, expected - read);
            if (n == 0) throw new InvalidDataException("PNG image data is too short.");
            read += n;
        }
        return raw;
    }

    static void Unfilter(byte[] raw, Header h)
    {
        var stride = h.Stride;
        var bpp = h.BytesPerPixel;

        for (var y = 0; y < h.Height; y++)
        {
            var row = y * (stride + 1);
            var filter = raw[row];
            var line = row + 1;
            var previous = y == 0 ? -1 : line - (stride + 1);

            for (var x = 0; x < stride; x++)
            {
                var left = x >= bpp ? raw[line + x - bpp] : 0;
                var up = previous >= 0 ? raw[previous + x] : 0;
                var upLeft = previous >= 0 && x >= bpp ? raw[previous + x - bpp] : 0;

                raw[line + x] = (byte)(raw[line + x] + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
                });
            }
        }
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

    static RgbaImage ToRgba(byte[] raw, Header h, byte[]? palette, byte[]? transparency)
    {
        var pixels = new byte[h.Width * h.Height * 4];
        var stride = h.Stride;
        var maxValue = (1 << Math.Min(h.BitDepth, 8)) - 1;

        for (var y = 0; y < h.Height; y++)
        {
            var line = y * (stride + 1) + 1;
            for (var x = 0; x < h.Width; x++)
            {
                var o = (y * h.Width + x) * 4;
                switch (h.ColorType)
                {
                    case 0:
                    {
                        var v = Sample(raw, line, x, h.BitDepth);
                        var g = Scale(v, h.BitDepth, maxValue);
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = g;
                        pixels[o + 3] = IsKeyed(transparency, v, 0) ? (byte)0 : (byte)255;
                        break;
                    }
                    case 2:
                    {
                        var r = Sample(raw, line, x * 3, h.BitDepth);
                        var g = Sample(raw, line, x * 3 + 1, h.BitDepth);
                        var b = Sample(raw, line, x * 3 + 2, h.BitDepth);
                        pixels[o] = Scale(r, h.BitDepth, maxValue);
                        pixels[o + 1] = Scale(g, h.BitDepth, maxValue);
                        pixels[o + 2] = Scale(b, h.BitDepth, maxValue);
                        pixels[o + 3] = IsKeyed(transparency, r, 0) && IsKeyed(transparency, g, 1) && IsKeyed(transparency, b, 2)
                            ? (byte)0
                            : (byte)255;
                        break;
                    }
                    case 3:
                    {
                        var index = Sample(raw, line, x, h.BitDepth);
                        if (index * 3 + 2 >= palette!.Length) throw new InvalidDataException("PNG palette index out of range.");
                        pixels[o] = palette[index * 3];
                        pixels[o + 1] = palette[index * 3 + 1];
                        pixels[o + 2] = palette[index * 3 + 2];
                        pixels[o + 3] = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
                        break;
                    }
                    case 4:
                    {
                        var g = Scale(Sample(raw, line, x * 2, h.BitDepth), h.BitDepth, maxValue);
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = g;
                        pixels[o + 3] = Scale(Sample(raw, line, x * 2 + 1, h.BitDepth), h.BitDepth, maxValue);
                        break;
                    }
                    default:
                        for (var c = 0; c < 4; c++)
                            pixels[o + c] = Scale(Sample(raw, line, x * 4 + c, h.BitDepth), h.BitDepth, maxValue);
                        break;
                }
            }
        }
        return new RgbaImage(h.Width, h.Height, pixels);
    }

    static int Sample(byte[] raw, int line, int index, int depth)
    {
        switch (depth)
        {
            case 8:
                return raw[line + index];
            case 16:
                return (raw[line + index * 2] << 8) | raw[line + index * 2 + 1];
            default:
                var bit = index * depth;
                var shift = 8 - depth - (bit % 8);
                return (raw[line + bit / 8] >> shift) & ((1 << depth) - 1);
        }
    }

    static byte Scale(int value, int depth, int maxValue) => depth switch
    {
        8 => (byte)value,
        16 => (byte)(value >> 8),
        _ => (byte)(value * 255 / maxValue),
    };

    static bool IsKeyed(byte[]? transparency, int sample, int channel)
    {
        if (transparency is null || transparency.Length < channel * 2 + 2) return false;
        return BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(channel * 2)) == sample;
    }
}

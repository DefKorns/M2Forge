namespace M2Forge.Core.Textures;

public static class TextureEncoder
{
    public static byte[] Encode(RgbaImage image, TextureFormat format)
    {
        var buffer = new byte[checked(image.Width * image.Height * format.BytesPerPixel())];
        WriteRegion(buffer, format, image.Width, image.Height, 0, 0, image);
        return buffer;
    }

    public static RgbaImage RoundTrip(RgbaImage image, TextureFormat format) =>
        RgbaImage.Decode(format, Encode(image, format), image.Width, image.Height);

    public static void WriteRegion(
        Span<byte> stream,
        TextureFormat format,
        int atlasWidth,
        int atlasHeight,
        int left,
        int top,
        RgbaImage region)
    {
        var bytesPerPixel = format.BytesPerPixel();
        if (stream.Length < (long)atlasWidth * atlasHeight * bytesPerPixel)
            throw new ArgumentException("The stream is smaller than the texture it should hold.", nameof(stream));
        if (left < 0 || top < 0 || left + region.Width > atlasWidth || top + region.Height > atlasHeight)
            throw new ArgumentOutOfRangeException(
                nameof(region),
                $"A {region.Width}x{region.Height} region at {left},{top} does not fit a {atlasWidth}x{atlasHeight} texture.");

        var source = region.Pixels;
        for (var y = 0; y < region.Height; y++)
        {
            var from = y * region.Width * 4;
            var to = ((top + y) * atlasWidth + left) * bytesPerPixel;

            for (var x = 0; x < region.Width; x++, from += 4, to += bytesPerPixel)
            {
                var r = source[from];
                var g = source[from + 1];
                var b = source[from + 2];
                var a = source[from + 3];

                switch (format)
                {
                    case TextureFormat.Rgba8:
                        stream[to] = r;
                        stream[to + 1] = g;
                        stream[to + 2] = b;
                        stream[to + 3] = a;
                        break;
                    case TextureFormat.A8L8:
                        stream[to] = Luma(r, g, b);
                        stream[to + 1] = a;
                        break;
                    case TextureFormat.A8:
                        stream[to] = a;
                        break;
                    case TextureFormat.L8:
                        stream[to] = Luma(r, g, b);
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }
        }
    }

    static byte Luma(byte r, byte g, byte b) => (byte)((2126 * r + 7152 * g + 722 * b + 5000) / 10000);
}

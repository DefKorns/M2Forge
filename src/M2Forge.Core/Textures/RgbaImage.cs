namespace M2Forge.Core.Textures;

public sealed class RgbaImage
{
    public RgbaImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Pixel buffer does not match the image size.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public static RgbaImage Decode(TextureFormat format, ReadOnlySpan<byte> data, int width, int height)
    {
        var count = checked(width * height);
        var expected = count * format.BytesPerPixel();
        if (data.Length < expected)
            throw new InvalidDataException($"Texture data has {data.Length} bytes, {format} {width}x{height} needs {expected}.");

        var rgba = new byte[count * 4];
        switch (format)
        {
            case TextureFormat.Rgba8:
                data[..expected].CopyTo(rgba);
                break;
            case TextureFormat.A8L8:
                for (var i = 0; i < count; i++)
                {
                    var l = data[i * 2];
                    var a = data[i * 2 + 1];
                    rgba[i * 4] = l;
                    rgba[i * 4 + 1] = l;
                    rgba[i * 4 + 2] = l;
                    rgba[i * 4 + 3] = a;
                }
                break;
            case TextureFormat.A8:
                for (var i = 0; i < count; i++)
                {
                    rgba[i * 4] = 255;
                    rgba[i * 4 + 1] = 255;
                    rgba[i * 4 + 2] = 255;
                    rgba[i * 4 + 3] = data[i];
                }
                break;
            case TextureFormat.L8:
                for (var i = 0; i < count; i++)
                {
                    var l = data[i];
                    rgba[i * 4] = l;
                    rgba[i * 4 + 1] = l;
                    rgba[i * 4 + 2] = l;
                    rgba[i * 4 + 3] = 255;
                }
                break;
            default:
                throw new NotSupportedException();
        }
        return new RgbaImage(width, height, rgba);
    }

    public RgbaImage Paste(RgbaImage source, int left, int top)
    {
        var pixels = (byte[])Pixels.Clone();
        for (var y = 0; y < source.Height; y++)
        {
            var ty = top + y;
            if (ty < 0 || ty >= Height) continue;

            var x0 = Math.Max(0, -left);
            var x1 = Math.Min(source.Width, Width - left);
            if (x1 <= x0) continue;

            Array.Copy(source.Pixels, (y * source.Width + x0) * 4, pixels, (ty * Width + left + x0) * 4, (x1 - x0) * 4);
        }
        return new RgbaImage(Width, Height, pixels);
    }

    public RgbaImage Crop(int left, int top, int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var sy = top + y;
            if (sy < 0 || sy >= Height) continue;

            var x0 = Math.Max(0, -left);
            var x1 = Math.Min(width, Width - left);
            if (x1 <= x0) continue;

            Array.Copy(Pixels, (sy * Width + left + x0) * 4, pixels, (y * width + x0) * 4, (x1 - x0) * 4);
        }
        return new RgbaImage(width, height, pixels);
    }

    public RgbaImage Thumbnail(int maxSide)
    {
        if (maxSide <= 0) throw new ArgumentOutOfRangeException(nameof(maxSide));
        if (Width <= maxSide && Height <= maxSide) return this;

        var scale = (double)maxSide / Math.Max(Width, Height);
        var width = Math.Max(1, (int)Math.Round(Width * scale));
        var height = Math.Max(1, (int)Math.Round(Height * scale));
        var pixels = new byte[width * height * 4];
        var stepX = (double)Width / width;
        var stepY = (double)Height / height;

        for (var y = 0; y < height; y++)
        {
            var y0 = y * stepY;
            var y1 = Math.Min(Height, (y + 1) * stepY);
            for (var x = 0; x < width; x++)
            {
                var x0 = x * stepX;
                var x1 = Math.Min(Width, (x + 1) * stepX);
                double r = 0, g = 0, b = 0, a = 0, total = 0;

                for (var sy = (int)y0; sy < Math.Ceiling(y1) && sy < Height; sy++)
                {
                    var wy = Math.Min(sy + 1, y1) - Math.Max(sy, y0);
                    for (var sx = (int)x0; sx < Math.Ceiling(x1) && sx < Width; sx++)
                    {
                        var w = wy * (Math.Min(sx + 1, x1) - Math.Max(sx, x0));
                        var i = (sy * Width + sx) * 4;
                        var alpha = Pixels[i + 3] / 255.0;
                        r += Pixels[i] * alpha * w;
                        g += Pixels[i + 1] * alpha * w;
                        b += Pixels[i + 2] * alpha * w;
                        a += alpha * w;
                        total += w;
                    }
                }

                var o = (y * width + x) * 4;
                if (a > 0)
                {
                    pixels[o] = (byte)Math.Round(r / a);
                    pixels[o + 1] = (byte)Math.Round(g / a);
                    pixels[o + 2] = (byte)Math.Round(b / a);
                }
                pixels[o + 3] = (byte)Math.Round(255 * a / total);
            }
        }
        return new RgbaImage(width, height, pixels);
    }
}

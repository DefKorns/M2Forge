namespace M2Forge.Core.Textures;

public static class TextureReplacer
{
    public static byte[] Replace(TextureSource source, ReadOnlySpan<byte> stream, RgbaImage image, IconInfo? icon = null)
    {
        var expected = checked(source.Width * source.Height * source.Format.BytesPerPixel());
        if (stream.Length < expected)
            throw new InvalidDataException($"The stream has {stream.Length} bytes, {source.Format} {source.Width}x{source.Height} needs {expected}.");

        var left = icon?.Left ?? 0;
        var top = icon?.Top ?? 0;
        var width = icon?.Width ?? source.Width;
        var height = icon?.Height ?? source.Height;

        if (image.Width != width || image.Height != height)
            throw new ArgumentException(
                $"The image is {image.Width}x{image.Height} but {(icon is null ? "the texture" : $"icon '{icon.Name}'")} is {width}x{height}.",
                nameof(image));

        var result = stream.ToArray();
        TextureEncoder.WriteRegion(result, source.Format, source.Width, source.Height, left, top, image);
        return result;
    }
}

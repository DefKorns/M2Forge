namespace M2Forge.Core.Textures;

public enum TextureFormat
{
    Rgba8,
    A8L8,
    A8,
    L8,
}

public static class TextureFormats
{
    public static TextureFormat Parse(string name) => name switch
    {
        "RGBA8" => TextureFormat.Rgba8,
        "A8L8" => TextureFormat.A8L8,
        "A8" => TextureFormat.A8,
        "L8" => TextureFormat.L8,
        _ => throw new NotSupportedException($"Unknown texture format '{name}'."),
    };

    public static int BytesPerPixel(this TextureFormat format) => format switch
    {
        TextureFormat.Rgba8 => 4,
        TextureFormat.A8L8 => 2,
        _ => 1,
    };
}

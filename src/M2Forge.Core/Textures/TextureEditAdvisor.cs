namespace M2Forge.Core.Textures;

public static class TextureEditAdvisor
{
    public static IReadOnlyList<string> Warnings(TextureFormat format, RgbaImage image)
    {
        var warnings = new List<string>();
        var pixels = image.Pixels;

        switch (format)
        {
            case TextureFormat.A8:
                if (AllOpaque(pixels))
                    warnings.Add("A8 stores only transparency and this image is fully opaque, so it will show as a solid block.");
                else
                    warnings.Add("A8 stores only transparency; the colours of the image are discarded.");
                break;
            case TextureFormat.A8L8:
            case TextureFormat.L8:
                if (HasColour(pixels))
                    warnings.Add($"{format} stores only grey levels; the colours of the image will be converted to grey.");
                break;
        }
        return warnings;
    }

    static bool AllOpaque(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 255) return false;
        return true;
    }

    static bool HasColour(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] != 0 && (pixels[i] != pixels[i + 1] || pixels[i + 1] != pixels[i + 2])) return true;
        return false;
    }
}

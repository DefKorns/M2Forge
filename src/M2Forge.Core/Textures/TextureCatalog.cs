using System.Text.Json;
using GMWare.M2.Psb;
using M2Forge.Core.Psb;
using Newtonsoft.Json;

namespace M2Forge.Core.Textures;

public sealed record IconInfo(string Name, int Left, int Top, int Width, int Height);

public sealed record TextureSource(
    string Name,
    TextureFormat Format,
    int Width,
    int Height,
    string StreamPath,
    IReadOnlyList<IconInfo> Icons,
    PsbStreamKind StreamKind = PsbStreamKind.Stream,
    uint StreamIndex = 0)
{
    public RgbaImage Load() => RgbaImage.Decode(Format, File.ReadAllBytes(StreamPath), Width, Height);
}

public static class TextureCatalog
{
    public static IReadOnlyList<TextureSource> Read(string psbJsonPath)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(psbJsonPath));
        return Parse(json.RootElement, StreamsDirectory(psbJsonPath));
    }

    public static IReadOnlyList<TextureSource> ReadPsb(byte[] psb)
    {
        using var source = new MemoryStream(psb);
        using var reader = new PsbReader(source);
        var root = reader.Root;

        using var text = new StringWriter();
        using (var writer = new JsonTextWriter(text)) root.WriteTo(writer);

        using var json = JsonDocument.Parse(text.ToString());
        return Parse(json.RootElement, string.Empty);
    }

    static IReadOnlyList<TextureSource> Parse(JsonElement root, string streamsDir)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("source", out var sources))
            return [];

        var result = new List<TextureSource>();
        foreach (var (name, holder) in EnumerateSources(sources))
        {
            if (holder.ValueKind != JsonValueKind.Object) continue;

            var texture = holder.TryGetProperty("texture", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? nested
                : holder;
            if (!texture.TryGetProperty("pixel", out var pixel) || pixel.ValueKind != JsonValueKind.String) continue;
            if (!texture.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) continue;

            var (kind, index) = ParseStream(pixel.GetString()!);
            result.Add(new TextureSource(
                name,
                TextureFormats.Parse(type.GetString()!),
                texture.GetProperty("width").GetInt32(),
                texture.GetProperty("height").GetInt32(),
                streamsDir.Length == 0 ? string.Empty : Path.Combine(streamsDir, StreamFileName(kind, index)),
                ReadIcons(holder),
                kind,
                index));
        }
        return result;
    }

    static IEnumerable<(string Name, JsonElement Value)> EnumerateSources(JsonElement sources)
    {
        switch (sources.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in sources.EnumerateObject())
                    yield return (property.Name, property.Value);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in sources.EnumerateArray())
                    yield return ((index++).ToString(), item);
                break;
        }
    }

    static string StreamsDirectory(string psbJsonPath)
    {
        const string suffix = ".json";
        if (!psbJsonPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a .psb.json file.", nameof(psbJsonPath));
        return psbJsonPath[..^suffix.Length] + ".streams";
    }

    static (PsbStreamKind Kind, uint Index) ParseStream(string reference)
    {
        if (reference.StartsWith("_stream:", StringComparison.Ordinal))
            return (PsbStreamKind.Stream, uint.Parse(reference["_stream:".Length..]));
        if (reference.StartsWith("_bstream:", StringComparison.Ordinal))
            return (PsbStreamKind.BStream, uint.Parse(reference["_bstream:".Length..]));
        throw new NotSupportedException($"Unknown stream reference '{reference}'.");
    }

    static string StreamFileName(PsbStreamKind kind, uint index) =>
        (kind == PsbStreamKind.Stream ? "stream_" : "bstream_") + index;

    static List<IconInfo> ReadIcons(JsonElement holder)
    {
        var icons = new List<IconInfo>();
        if (!holder.TryGetProperty("icon", out var iconSet) || iconSet.ValueKind != JsonValueKind.Object) return icons;

        foreach (var icon in iconSet.EnumerateObject())
        {
            var value = icon.Value;
            if (value.ValueKind != JsonValueKind.Object) continue;

            var scale = value.TryGetProperty("resolution", out var resolution) && resolution.ValueKind == JsonValueKind.Number
                ? resolution.GetDouble()
                : 1.0;
            icons.Add(new IconInfo(
                icon.Name,
                (int)value.GetProperty("left").GetDouble(),
                (int)value.GetProperty("top").GetDouble(),
                (int)(value.GetProperty("width").GetDouble() * scale),
                (int)(value.GetProperty("height").GetDouble() * scale)));
        }
        return icons;
    }
}

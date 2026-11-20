using System.Text.Json;
using M2Forge.Core.Textures;

namespace M2Forge.Core.Roms;

public enum CoverKind
{
    Spine,
    Cover,
    BigCover,
}

public sealed class RomArtwork
{
    readonly string motionFolder;
    readonly Dictionary<string, Set?> sets = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();

    public RomArtwork(string regionFolder)
    {
        motionFolder = Path.Combine(regionFolder, "motion");
    }

    public IReadOnlyList<string> AvailableSets =>
        Directory.Exists(motionFolder)
            ? Directory.EnumerateFiles(motionFolder, "*titleselect_*.psb.json")
                .Select(f => SetName(Path.GetFileName(f)))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

    public string SetForRegion(string region)
    {
        var wanted = region.ToLowerInvariant();
        var available = AvailableSets;
        return available.Contains(wanted) ? wanted : available.FirstOrDefault() ?? wanted;
    }

    public RgbaImage? Get(MenuEntry entry, CoverKind kind, string set)
    {
        if (entry.Image == RomCatalog.NoImage) return null;

        foreach (var candidate in AvailableSets.Where(s => !s.Equals(set, StringComparison.OrdinalIgnoreCase)).Prepend(set))
        {
            var image = GetFrom(entry, kind, candidate);
            if (image is not null) return image;
        }
        return null;
    }

    RgbaImage? GetFrom(MenuEntry entry, CoverKind kind, string set)
    {
        lock (gate)
        {
            var loaded = LoadSet(set);
            if (loaded is null) return null;

            foreach (var name in MotionNames(kind, set))
            {
                if (!loaded.Motions.TryGetValue(name, out var frames)) continue;
                if (!frames.TryGetValue(entry.Image, out var frame)) continue;
                if (!loaded.Textures.TryGetValue(frame.Texture, out var texture)) continue;

                var icon = texture.Source.Icons.FirstOrDefault(i => i.Name == frame.Icon);
                if (icon is not null) return texture.Pixels.Value.Crop(icon.Left, icon.Top, icon.Width, icon.Height);
            }
            return null;
        }
    }

    static string[] MotionNames(CoverKind kind, string set) =>
        kind switch
        {
            CoverKind.Spine => [$"side_{set}", "side"],
            CoverKind.Cover => [$"front_{set}", "front"],
            _ => ["bigfront", $"front_{set}", "front"],
        };

    static string SetName(string fileName)
    {
        var name = fileName[..^".psb.json".Length];
        return name[(name.LastIndexOf('_') + 1)..];
    }

    Set? LoadSet(string name)
    {
        if (sets.TryGetValue(name, out var cached)) return cached;

        Set? loaded = null;
        var file = Directory.Exists(motionFolder)
            ? Directory.EnumerateFiles(motionFolder, "*titleselect_" + name + ".psb.json").FirstOrDefault()
            : null;
        if (file is not null)
        {
            try
            {
                loaded = new Set(ReadMotions(file), TextureCatalog.Read(file).ToDictionary(s => s.Name, s => new Texture(s)));
            }
            catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidDataException)
            {
                loaded = null;
            }
        }
        sets[name] = loaded;
        return loaded;
    }

    static Dictionary<string, Dictionary<int, Frame>> ReadMotions(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var motions = json.RootElement.GetProperty("object").GetProperty("pkgData").GetProperty("motion");

        var result = new Dictionary<string, Dictionary<int, Frame>>();
        foreach (var motion in motions.EnumerateObject())
        {
            if (motion.Value.ValueKind != JsonValueKind.Object || !motion.Value.TryGetProperty("layer", out var layers)) continue;

            var best = new Dictionary<int, Frame>();
            Visit(layers, ref best);
            result[motion.Name] = best;
        }
        return result;
    }

    static void Visit(JsonElement layers, ref Dictionary<int, Frame> best)
    {
        foreach (var layer in layers.EnumerateArray())
        {
            var own = FramesOf(layer);
            if (own.Count > best.Count) best = own;
            if (layer.TryGetProperty("children", out var children)) Visit(children, ref best);
        }
    }

    static Dictionary<int, Frame> FramesOf(JsonElement layer)
    {
        var frames = new Dictionary<int, Frame>();
        if (!layer.TryGetProperty("frameList", out var list)) return frames;

        foreach (var frame in list.EnumerateArray())
        {
            if (!frame.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object) continue;
            if (!content.TryGetProperty("icon", out var icon) || icon.ValueKind != JsonValueKind.String) continue;
            if (!content.TryGetProperty("src", out var src) || src.ValueKind != JsonValueKind.String) continue;
            if (!frame.TryGetProperty("time", out var time) || !time.TryGetDouble(out var at) || at != Math.Floor(at)) continue;

            frames.TryAdd((int)at, new Frame(icon.GetString()!, src.GetString()!));
        }
        return frames;
    }

    sealed record Frame(string Icon, string Texture);

    sealed record Set(Dictionary<string, Dictionary<int, Frame>> Motions, Dictionary<string, Texture> Textures);

    sealed class Texture(TextureSource source)
    {
        public TextureSource Source { get; } = source;
        public Lazy<RgbaImage> Pixels { get; } = new(source.Load);
    }
}

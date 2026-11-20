using System.Text.Json;
using System.Text.RegularExpressions;

namespace M2Forge.Core.Roms;

public sealed record MenuEntry(
    string Tag,
    string MenuRegion,
    string MenuLanguage,
    string Country,
    string Title,
    int Image);

public sealed record RomDetails(string Title, string? NativeTitle, IReadOnlyList<MenuEntry> Entries)
{
    public MenuEntry? CoverEntry =>
        Entries.FirstOrDefault(e => e.MenuLanguage == "EN" && e.Image != RomCatalog.NoImage) ??
        Entries.FirstOrDefault(e => e.Image != RomCatalog.NoImage);
}

public sealed partial class RomCatalog
{
    public const int NoImage = 99;

    readonly Dictionary<string, RomDetails> byRom;

    RomCatalog(string regionFolder, Dictionary<string, RomDetails> byRom)
    {
        RegionFolder = regionFolder;
        this.byRom = byRom;
    }

    public string RegionFolder { get; }

    public int Count => byRom.Count;

    public static RomCatalog Empty { get; } = new(string.Empty, []);

    public RomDetails? Find(string romFileName) =>
        byRom.GetValueOrDefault(romFileName);

    public static RomCatalog Load(string extractedRoot)
    {
        var region = FindRegionFolder(extractedRoot);
        if (region is null) return Empty;

        var config = Path.Combine(region, "config");
        var profile = Path.Combine(config, "title_prof.psb.json");
        var top = Path.Combine(config, "title_mode_top.psb.json");
        if (!File.Exists(profile) || !File.Exists(top)) return Empty;

        try
        {
            var roms = ReadRoms(profile);
            var titles = ReadTitles(top);
            return new RomCatalog(region, Combine(roms, titles));
        }
        catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            return Empty;
        }
    }

    public static string? FindRegionFolder(string extractedRoot)
    {
        if (!Directory.Exists(extractedRoot)) return null;

        return Directory.EnumerateDirectories(extractedRoot)
            .Where(d => RegionFolderName().IsMatch(Path.GetFileName(d)))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "config", "title_mode_top.psb.json")));
    }

    static Dictionary<string, (string Rom, string Country)> ReadRoms(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, (string, string)>();
        Walk(json.RootElement, null, result);
        return result;

        static void Walk(JsonElement element, string? name, Dictionary<string, (string, string)> result)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (name is not null &&
                        element.TryGetProperty("rom", out var rom) && rom.ValueKind == JsonValueKind.String)
                    {
                        var country = element.TryGetProperty("country", out var c) && c.ValueKind == JsonValueKind.String
                            ? c.GetString()!
                            : string.Empty;
                        result[name] = (Path.GetFileName(rom.GetString()!), country);
                    }
                    foreach (var property in element.EnumerateObject()) Walk(property.Value, property.Name, result);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Walk(item, name, result);
                    break;
            }
        }
    }

    static Dictionary<string, (string Title, int Image)> ReadTitles(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, (string, int)>();
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!item.TryGetProperty("regionTag", out var tag) || tag.ValueKind != JsonValueKind.String) continue;

            var title = item.TryGetProperty("tname", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : string.Empty;
            var image = item.TryGetProperty("image", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : NoImage;
            var key = tag.GetString()!;
            if (!result.TryGetValue(key, out var existing) || (existing.Item1.Length == 0 && title.Length > 0))
                result[key] = (title, image);
        }
        return result;
    }

    static Dictionary<string, RomDetails> Combine(
        Dictionary<string, (string Rom, string Country)> roms,
        Dictionary<string, (string Title, int Image)> titles)
    {
        var grouped = new Dictionary<string, List<MenuEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tag, (rom, country)) in roms)
        {
            var parts = tag.Split('_');
            if (parts.Length < 3) continue;

            var known = titles.TryGetValue(tag, out var info);
            var title = info.Title ?? string.Empty;
            var image = known ? info.Image : NoImage;
            if (!known && FindBaseTag(parts, titles) is { } baseTag)
            {
                image = titles[baseTag].Image;
                title = string.Empty;
            }

            var entry = new MenuEntry(tag, parts[0].ToUpperInvariant(), parts[1].ToUpperInvariant(), country, title, image);

            if (!grouped.TryGetValue(rom, out var list)) grouped[rom] = list = [];
            list.Add(entry);
        }

        var result = new Dictionary<string, RomDetails>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rom, entries) in grouped)
        {
            var ordered = entries.OrderBy(e => e.MenuLanguage == "EN" ? 0 : 1).ThenBy(e => e.Tag, StringComparer.Ordinal).ToList();
            var latin = ordered.FirstOrDefault(e => e.Title.Length > 0 && !HasNonLatin(e.Title));
            var native = ordered.FirstOrDefault(e => e.Title.Length > 0 && HasNonLatin(e.Title));
            result[rom] = new RomDetails(latin?.Title ?? Prettify(rom), native?.Title, ordered);
        }
        return result;
    }

    static string? FindBaseTag(string[] parts, Dictionary<string, (string Title, int Image)> titles)
    {
        var slug = string.Join('_', parts[2..]);
        string? best = null;
        foreach (var candidate in titles.Keys)
        {
            var other = candidate.Split('_');
            if (other.Length != parts.Length || !other[0].Equals(parts[0], StringComparison.OrdinalIgnoreCase)) continue;

            var otherSlug = string.Join('_', other[2..]);
            if (!DiffersByOneTwo(slug, otherSlug)) continue;

            if (other[1].Equals(parts[1], StringComparison.OrdinalIgnoreCase)) return candidate;
            best ??= candidate;
        }
        return best;
    }

    static bool DiffersByOneTwo(string alternate, string baseSlug)
    {
        if (alternate.Length != baseSlug.Length) return false;

        var differences = 0;
        for (var i = 0; i < alternate.Length; i++)
        {
            if (char.ToLowerInvariant(alternate[i]) == char.ToLowerInvariant(baseSlug[i])) continue;
            if (alternate[i] != '2') return false;
            differences++;
        }
        return differences == 1;
    }

    public static string Prettify(string romFileName)
    {
        var (_, _, title) = RomLibrary.ParseName(Path.GetFileNameWithoutExtension(romFileName));
        return string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 0 && char.IsAsciiLetterLower(w[0]) ? char.ToUpperInvariant(w[0]) + w[1..] : w));
    }

    static bool HasNonLatin(string text) => text.Any(c => c > 0x24F);

    [GeneratedRegex(@"^\d{3}$")]
    private static partial Regex RegionFolderName();
}

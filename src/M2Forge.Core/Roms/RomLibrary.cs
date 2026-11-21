using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace M2Forge.Core.Roms;

public static partial class RomLibrary
{
    public const string MegaDriveFormat = "bin";

    public static readonly IReadOnlyList<string> Formats = [MegaDriveFormat, "smd", "smp", "mcd"];

    const int HeaderEnd = 0x200;

    public static IReadOnlyList<RomInfo> Scan(string extractedRoot)
    {
        var folder = System.IO.Path.Combine(extractedRoot, "system", "roms");
        if (!Directory.Exists(folder)) return [];

        var roms = new List<RomInfo>();
        foreach (var path in Directory.EnumerateFiles(folder).Where(IsRom))
        {
            var info = new FileInfo(path);
            var (region, language, title) = ParseName(System.IO.Path.GetFileNameWithoutExtension(path));

            RomHeader? header = null;
            if (info.Length >= HeaderEnd && info.Extension.Equals("." + MegaDriveFormat, StringComparison.OrdinalIgnoreCase))
            {
                var head = new byte[HeaderEnd];
                using (var stream = File.OpenRead(path)) stream.ReadExactly(head);
                header = ReadHeader(head);
            }
            roms.Add(new RomInfo(info.Name, path, region, language, title, info.Length, header));
        }

        return roms
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Region, StringComparer.Ordinal)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ToList();
    }

    static bool IsRom(string path) =>
        Formats.Contains(System.IO.Path.GetExtension(path).TrimStart('.'), StringComparer.OrdinalIgnoreCase);

    public static (string Region, string Language, string Title) ParseName(string baseName)
    {
        var parts = baseName.Split('_');
        var hasPrefix = parts.Length > 2 && parts[0].Length == 2 && parts[1].Length == 2 &&
                        parts[0].All(char.IsAsciiLetterLower) && parts[1].All(char.IsAsciiLetterLower);

        var region = hasPrefix ? parts[0] : string.Empty;
        var language = hasPrefix ? parts[1] : string.Empty;
        var raw = string.Join(' ', hasPrefix ? parts[2..] : parts);
        return (region, language, Apostrophes().Replace(raw, "'s"));
    }

    public static RomHeader? ReadHeader(ReadOnlySpan<byte> rom)
    {
        if (rom.Length < HeaderEnd) return null;

        var console = Text(rom, 0x100, 16);
        if (!console.Contains("SEGA", StringComparison.OrdinalIgnoreCase)) return null;

        return new RomHeader(
            console,
            Text(rom, 0x110, 16),
            Text(rom, 0x120, 48),
            Text(rom, 0x150, 48),
            Text(rom, 0x180, 14),
            BinaryPrimitives.ReadUInt16BigEndian(rom[0x18E..]),
            Text(rom, 0x1F0, 3));
    }

    static string Text(ReadOnlySpan<byte> rom, int offset, int length)
    {
        var builder = new StringBuilder(length);
        foreach (var b in rom.Slice(offset, length))
        {
            if (b == 0) continue;
            builder.Append(b is >= 0x20 and < 0x7F ? (char)b : ' ');
        }

        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"(?<=\w) s(?=\s|$)")]
    private static partial Regex Apostrophes();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex Spaces();
}

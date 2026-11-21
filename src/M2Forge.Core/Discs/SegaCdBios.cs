using System.Text;
using M2Forge.Core.Roms;

namespace M2Forge.Core.Discs;

public static class SegaCdBios
{
    const int HeaderSize = 0x200;
    const int RegionOffset = 0x1F0;

    public static string? RetroArchName(string path)
    {
        var header = ReadHeader(path);
        if (header is null) return null;

        var text = Encoding.ASCII.GetString(header, 0x100, 0x80);
        if (!text.Contains("BOOT ROM", StringComparison.Ordinal) || text.Contains("WONDER", StringComparison.Ordinal)) return null;

        return (char)header[RegionOffset] switch
        {
            'U' => "bios_CD_U.bin",
            'E' => "bios_CD_E.bin",
            'J' => "bios_CD_J.bin",
            _ => null,
        };
    }

    public static IReadOnlyList<SegaCdBiosFile> Select(IEnumerable<RomInfo> roms) =>
        roms.Where(r => r.Format == RomLibrary.MegaDriveFormat && r.FileName.Contains("bios", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(r => RetroArchName(r.Path) is { } name ? new SegaCdBiosFile(r, name) : null)
            .OfType<SegaCdBiosFile>()
            .GroupBy(b => b.RetroArchName)
            .Select(g => g.First())
            .OrderBy(b => b.RetroArchName, StringComparer.Ordinal)
            .ToList();

    public static string NameForRegion(string region) => region.ToLowerInvariant() switch
    {
        "us" => "bios_CD_U.bin",
        "eu" => "bios_CD_E.bin",
        "jp" => "bios_CD_J.bin",
        _ => "bios_CD_U.bin",
    };

    public static RomExportResult Export(IEnumerable<SegaCdBiosFile> files, string outputDirectory, bool overwrite)
    {
        Directory.CreateDirectory(outputDirectory);

        var exported = 0;
        var skipped = 0;
        long bytes = 0;
        var failures = new List<string>();
        foreach (var file in files)
        {
            var target = Path.Combine(outputDirectory, file.RetroArchName);
            if (File.Exists(target) && !overwrite)
            {
                skipped++;
                continue;
            }

            try
            {
                File.Copy(file.Rom.Path, target, overwrite: true);
                exported++;
                bytes += file.Rom.Size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{file.RetroArchName}: {ex.Message}");
            }
        }
        return new RomExportResult(exported, skipped, bytes, failures);
    }

    static byte[]? ReadHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < HeaderSize) return null;
            var header = new byte[HeaderSize];
            stream.ReadExactly(header);
            return header;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

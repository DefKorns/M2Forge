using System.Security.Cryptography;
using System.IO.Hashing;

namespace M2Forge.Core.Roms;

public static class RomExporter
{
    public static string ExportName(RomInfo rom, string extension = ".bin")
    {
        var chosen = rom.IsMegaDriveRom ? extension : "." + rom.Format;
        return System.IO.Path.GetFileNameWithoutExtension(rom.FileName) + (chosen.StartsWith('.') ? chosen : "." + chosen);
    }

    public static RomExportResult Export(
        IEnumerable<RomInfo> roms,
        string outputDirectory,
        string extension = ".bin",
        bool overwrite = false,
        IProgress<string>? progress = null,
        bool zip = false)
    {
        Directory.CreateDirectory(outputDirectory);

        var exported = 0;
        var skipped = 0;
        long bytes = 0;
        var failures = new List<string>();
        foreach (var rom in roms)
        {
            var entryName = ExportName(rom, extension);
            var target = System.IO.Path.Combine(outputDirectory, zip ? System.IO.Path.ChangeExtension(entryName, ".zip") : entryName);
            if (File.Exists(target) && !overwrite)
            {
                skipped++;
                continue;
            }

            try
            {
                progress?.Report($"Exporting {System.IO.Path.GetFileName(target)}");
                if (zip) WriteZip(rom.Path, entryName, target);
                else File.Copy(rom.Path, target, overwrite: true);
                exported++;
                bytes += rom.Size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{rom.FileName}: {ex.Message}");
            }
        }
        return new RomExportResult(exported, skipped, bytes, failures);
    }

    static void WriteZip(string source, string entryName, string target)
    {
        var temp = target + ".part";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                using var input = File.OpenRead(source);
                using var output = entry.Open();
                input.CopyTo(output);
            }
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static bool IsDisc(RomInfo rom) => rom.Format == "mcd";

    public static async Task<RomExportResult> ExportAsync(
        IEnumerable<RomInfo> roms,
        string outputDirectory,
        string extension = ".bin",
        bool overwrite = false,
        string? chdmanPath = null,
        IProgress<string>? progress = null,
        CancellationToken cancellation = default,
        bool zipCartridges = false)
    {
        var list = roms.ToList();
        var plain = Export(list.Where(r => !IsDisc(r)), outputDirectory, extension, overwrite, progress, zipCartridges);

        var exported = plain.Exported;
        var skipped = plain.Skipped;
        var bytes = plain.Bytes;
        var failures = plain.Failures.ToList();

        foreach (var rom in list.Where(IsDisc))
        {
            cancellation.ThrowIfCancellationRequested();
            var stem = System.IO.Path.GetFileNameWithoutExtension(rom.FileName);
            var basePath = System.IO.Path.Combine(outputDirectory, stem);
            var finalExtension = chdmanPath is null ? ".cue" : ".chd";
            var existing = Directory.Exists(outputDirectory)
                ? Directory.GetFiles(outputDirectory, stem + "*" + finalExtension)
                : [];
            if (existing.Length > 0 && !overwrite)
            {
                skipped++;
                continue;
            }

            try
            {
                progress?.Report($"Converting {rom.FileName} to {(chdmanPath is null ? "BIN/CUE" : "CHD")}");
                var images = await Discs.DiscConverter.ConvertAsync(rom.Path, basePath, cancellation).ConfigureAwait(false);
                foreach (var image in images)
                {
                    bytes += new FileInfo(image.BinPath).Length;
                    if (chdmanPath is null) continue;

                    var chd = System.IO.Path.ChangeExtension(image.CuePath, ".chd");
                    await Discs.Chdman.CompressAsync(chdmanPath, image.CuePath, chd, cancellation).ConfigureAwait(false);
                    File.Delete(image.CuePath);
                    File.Delete(image.BinPath);
                }
                exported++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failures.Add($"{rom.FileName}: {ex.Message}");
            }
        }
        return new RomExportResult(exported, skipped, bytes, failures);
    }

    public static RomHashes ComputeHashes(string path)
    {
        var data = File.ReadAllBytes(path);
        var crc = Convert.ToHexString(Crc32.Hash(data).Reverse().ToArray()).ToLowerInvariant();
        var sha1 = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
        return new RomHashes(crc, sha1);
    }
}

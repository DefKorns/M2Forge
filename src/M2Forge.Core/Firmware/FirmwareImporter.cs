using System.IO.Compression;
using SharpCompress.Archives.SevenZip;

namespace M2Forge.Core.Firmware;

public sealed record ImportResult(string Folder, IReadOnlyList<string> Files, ConsoleKind Console);

public static class FirmwareImporter
{
    public static readonly string[] Wanted = ["alldata.psb.m", "alldata.bin", "m2engage"];

    const string Mini1Folder = "usr/game";
    const string Mini2Folder = "usr/games";

    static readonly string[] GameFolders = [Mini1Folder, Mini2Folder];
    const int ExtMagicOffset = 0x438;

    public static bool IsArchive(string path) =>
        File.Exists(path) && path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    public static ImportResult Import(string archivePath, string outputFolder, IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(outputFolder);
        var temp = Path.Combine(Path.GetTempPath(), "m2forge-nand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var archive = SevenZipArchive.OpenArchive(archivePath);
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory || entry.Key is null || !entry.Key.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    continue;

                progress?.Report("Reading " + Path.GetFileName(entry.Key));
                var image = Path.Combine(temp, "partition.img");
                var packed = Path.Combine(temp, "partition.gz");
                using (var raw = reader.OpenEntryStream())
                using (var file = File.Create(packed))
                    raw.CopyTo(file);
                if (!TryInflateExt(packed, image)) continue;

                var (files, console) = CopyGameFiles(image, outputFolder, progress);
                if (files.Count > 0) return new ImportResult(outputFolder, files, console);
            }
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }

        throw new InvalidDataException("No partition with usr/game/alldata.bin was found in " + Path.GetFileName(archivePath));
    }

    public static bool IsDiskImage(string path) =>
        File.Exists(path) && path.EndsWith(".img", StringComparison.OrdinalIgnoreCase);

    public static ConsoleKind? DetectDiskImage(string imagePath)
    {
        foreach (var partition in DiskPartitions.Read(imagePath))
        {
            if (partition.Type != 0x83) continue;

            ExtImage fs;
            try { fs = new ExtImage(imagePath, partition.Offset); }
            catch (InvalidDataException) { continue; }

            using (fs)
            {
                var folder = FindGameFolder(fs);
                if (folder is not null) return folder == Mini2Folder ? ConsoleKind.MegaDriveMini2 : ConsoleKind.MegaDriveMini;
            }
        }
        return null;
    }

    public static ImportResult ImportDiskImage(string imagePath, string outputFolder, IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(outputFolder);
        foreach (var partition in DiskPartitions.Read(imagePath))
        {
            if (partition.Type != 0x83) continue;

            progress?.Report($"Looking at partition {partition.Number}");
            ExtImage fs;
            try { fs = new ExtImage(imagePath, partition.Offset); }
            catch (InvalidDataException) { continue; }

            using (fs)
            {
                if (FindGameFolder(fs) is null) continue;
                progress?.Report($"Found the firmware files in partition {partition.Number}");
                var (files, console) = CopyFiles(fs, outputFolder, progress);
                if (files.Count > 0) return new ImportResult(outputFolder, files, console);
            }
        }

        throw new InvalidDataException("No partition with usr/game/alldata.bin was found in " + Path.GetFileName(imagePath));
    }

    static bool TryInflateExt(string packed, string target)
    {
        using var source = File.OpenRead(packed);
        var isGzip = source.ReadByte() == 0x1F && source.ReadByte() == 0x8B;
        source.Position = 0;
        using Stream gzip = isGzip ? new GZipStream(source, CompressionMode.Decompress) : source;
        var head = new byte[ExtMagicOffset + 2];
        var read = 0;
        while (read < head.Length)
        {
            var n = gzip.Read(head, read, head.Length - read);
            if (n == 0) return false;
            read += n;
        }
        if (head[ExtMagicOffset] != 0x53 || head[ExtMagicOffset + 1] != 0xEF) return false;

        using var output = File.Create(target);
        output.Write(head);
        gzip.CopyTo(output);
        return true;
    }

    static (List<string> Files, ConsoleKind Console) CopyGameFiles(string imagePath, string outputFolder, IProgress<string>? progress)
    {
        using var fs = new ExtImage(imagePath);
        return CopyFiles(fs, outputFolder, progress);
    }

    static string? FindGameFolder(ExtImage fs) =>
        GameFolders.FirstOrDefault(folder => fs.FileExists(folder + "/alldata.bin"));

    static (List<string> Files, ConsoleKind Console) CopyFiles(ExtImage fs, string outputFolder, IProgress<string>? progress)
    {
        var copied = new List<string>();
        var folder = FindGameFolder(fs);
        if (folder is null) return (copied, ConsoleKind.MegaDriveMini);

        var console = folder == Mini2Folder ? ConsoleKind.MegaDriveMini2 : ConsoleKind.MegaDriveMini;

        foreach (var name in Wanted)
        {
            var source = folder + "/" + name;
            if (!fs.FileExists(source)) continue;

            progress?.Report("Copying " + name);
            using var output = File.Create(Path.Combine(outputFolder, name));
            fs.CopyTo(source, output);
            copied.Add(name);
        }
        return (copied.Contains("alldata.bin") ? copied : [], console);
    }
}

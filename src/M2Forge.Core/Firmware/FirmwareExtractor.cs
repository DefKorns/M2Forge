using GMWare.M2.MArchive;
using GMWare.M2.Psb;
using M2Forge.Core.Psb;

namespace M2Forge.Core.Firmware;

public sealed class FirmwareExtractor(ConsoleProfile profile, string seed)
{
    readonly MArchivePacker packer = new(CodecFactory.Create(profile.Codec), seed, profile.KeyLength);

    public ExtractionResult Extract(string alldataPath, string outputDir, IProgress<string>? progress = null)
    {
        var failures = new List<string>();
        var files = UnpackArchive(alldataPath, outputDir, progress);

        var archives = 0;
        foreach (var file in Directory.EnumerateFiles(outputDir, "*.m", SearchOption.AllDirectories).ToList())
        {
            progress?.Report($"Decompressing {Path.GetRelativePath(outputDir, file)}");
            try
            {
                packer.DecompressFile(file);
                archives++;
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.Message}");
            }
        }

        var psbs = 0;
        foreach (var file in Directory.EnumerateFiles(outputDir, "*.psb", SearchOption.AllDirectories).ToList())
        {
            progress?.Report($"Deserializing {Path.GetRelativePath(outputDir, file)}");
            try
            {
                DumpPsb(file);
                psbs++;
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.Message}");
            }
        }

        return new ExtractionResult(files, archives, psbs, failures);
    }

    int UnpackArchive(string alldataPath, string outputDir, IProgress<string>? progress)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(alldataPath))!;
        var binPath = Path.Combine(dir, "alldata.bin");
        var archive = AllDataArchive.Open(alldataPath, binPath, new MArchiveBytes(profile, seed));

        var count = 0;
        using var bin = File.OpenRead(binPath);
        foreach (var entry in archive.Entries)
        {
            progress?.Report($"Extracting {entry.Path}");
            var target = Path.Combine(outputDir, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = File.Create(target);
            bin.Seek(entry.Offset, SeekOrigin.Begin);
            CopyExactly(bin, output, entry.Length);
            count++;
        }
        return count;
    }

    static void CopyExactly(Stream source, Stream destination, long length)
    {
        var buffer = new byte[81920];
        while (length > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0) throw new EndOfStreamException();
            destination.Write(buffer, 0, read);
            length -= read;
        }
    }

    static void DumpPsb(string psbPath)
    {
        using var fs = File.OpenRead(psbPath);
        using var reader = new PsbReader(fs, null, null);
        File.WriteAllText(psbPath + ".json", PsbDump.ToJson(reader.Root));

        if (reader.StreamCache.Count == 0 && reader.BStreamCache.Count == 0) return;

        var streams = psbPath + ".streams";
        Directory.CreateDirectory(streams);
        foreach (var s in reader.StreamCache)
            File.WriteAllBytes(Path.Combine(streams, "stream_" + s.Key), s.Value.BinaryData);
        foreach (var s in reader.BStreamCache)
            File.WriteAllBytes(Path.Combine(streams, "bstream_" + s.Key), s.Value.BinaryData);
    }
}

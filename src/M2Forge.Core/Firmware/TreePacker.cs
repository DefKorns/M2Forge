using M2Forge.Core.Psb;

namespace M2Forge.Core.Firmware;

public static class TreePacker
{
    public static string RelativePath(string entry) =>
        entry.EndsWith(".m", StringComparison.OrdinalIgnoreCase) ? entry[..^2] : entry;

    public static TreeScan Scan(AllDataArchive archive, MArchiveBytes codec, string extractedRoot, IProgress<string>? progress = null)
    {
        var changes = new List<TreeChange>();
        var replacements = new Dictionary<string, byte[]>();
        var warnings = new List<string>();
        var issues = new List<string>();
        var missing = 0;
        var compared = 0;

        foreach (var entry in archive.Entries)
        {
            var relative = RelativePath(entry.Path);
            var file = Path.Combine(extractedRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file))
            {
                missing++;
                continue;
            }

            compared++;
            byte[] original;
            try
            {
                original = archive.ReadDecompressed(entry.Path);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException)
            {
                issues.Add($"{entry.Path}: cannot read the original: {ex.Message}");
                continue;
            }

            var current = File.ReadAllBytes(file);
            WarnAboutDerivedFiles(file, warnings);
            if (current.AsSpan().SequenceEqual(original)) continue;

            progress?.Report($"Compressing {relative}");
            byte[] stored;
            if (entry.Path.EndsWith(".m", StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(entry.Path);
                stored = codec.Compress(name, current);
                if (!codec.Decompress(name, stored).AsSpan().SequenceEqual(current))
                {
                    issues.Add($"{entry.Path}: the recompressed data does not decompress back to the edited file.");
                    continue;
                }
            }
            else
            {
                stored = current;
            }

            replacements[entry.Path] = stored;
            changes.Add(new TreeChange(entry.Path, relative, original.Length, current.Length));
        }

        if (missing > 0)
            warnings.Add($"{missing} file(s) are not in the extracted folder; the originals are kept.");

        return new TreeScan(changes, replacements, warnings, issues, missing, compared);
    }

    static void WarnAboutDerivedFiles(string file, List<string> warnings)
    {
        if (!file.EndsWith(".psb", StringComparison.OrdinalIgnoreCase)) return;

        var json = file + ".json";
        if (!File.Exists(json)) return;

        try
        {
            if (!PsbDump.SameText(File.ReadAllText(json), PsbDump.ToJson(file)))
                warnings.Add($"{Path.GetFileName(json)} does not match {Path.GetFileName(file)}; only the .psb itself is packed, edits to the JSON and the stream files are ignored.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or Newtonsoft.Json.JsonException or ArgumentException or EndOfStreamException)
        {
        }
    }
}

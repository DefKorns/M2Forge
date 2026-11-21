using GMWare.M2.Psb;
using Newtonsoft.Json.Linq;

namespace M2Forge.Core.Firmware;

public sealed class AllDataArchive
{
    public const int Alignment = 2048;
    const ushort ManifestPsbVersion = 3;

    readonly string manifestPath;
    readonly string binPath;
    readonly MArchiveBytes archive;
    readonly byte[] manifestFile;
    readonly JObject manifest;

    AllDataArchive(string manifestPath, string binPath, MArchiveBytes archive)
    {
        this.manifestPath = manifestPath;
        this.binPath = binPath;
        this.archive = archive;

        manifestFile = File.ReadAllBytes(manifestPath);
        var psb = manifestPath.EndsWith(".m", StringComparison.OrdinalIgnoreCase)
            ? archive.Decompress(System.IO.Path.GetFileName(manifestPath), manifestFile)
            : manifestFile;

        using var source = new MemoryStream(psb);
        using var reader = new PsbReader(source);
        manifest = reader.Root as JObject ?? throw new InvalidDataException("The archive manifest is not an object.");
        if (manifest.Value<string>("id") != "archive" || manifest["file_info"] is not JObject info)
            throw new InvalidDataException("The PSB file is not an archive manifest.");

        Entries = info.Properties()
            .Select(p => new AllDataEntry(p.Name, p.Value[0]!.Value<long>(), p.Value[1]!.Value<long>()))
            .OrderBy(e => e.Offset)
            .ToList();
    }

    public IReadOnlyList<AllDataEntry> Entries { get; }

    public AllDataEntry? TryGet(string path) => Entries.FirstOrDefault(e => e.Path == path);

    public static AllDataArchive Open(string manifestPath, string binPath, MArchiveBytes archive) =>
        new(manifestPath, binPath, archive);

    public byte[] ReadStored(string path)
    {
        var entry = Find(path);
        using var bin = File.OpenRead(binPath);
        bin.Seek(entry.Offset, SeekOrigin.Begin);
        var bytes = new byte[entry.Length];
        bin.ReadExactly(bytes);
        return bytes;
    }

    public byte[] ReadDecompressed(string path)
    {
        var stored = ReadStored(path);
        return path.EndsWith(".m", StringComparison.OrdinalIgnoreCase)
            ? archive.Decompress(System.IO.Path.GetFileName(path), stored)
            : stored;
    }

    public AllDataBuildResult Build(
        string outputDirectory,
        IReadOnlyDictionary<string, byte[]>? replacements = null,
        IProgress<string>? progress = null)
    {
        replacements ??= new Dictionary<string, byte[]>();
        foreach (var key in replacements.Keys) Find(key);

        var outputBin = System.IO.Path.Combine(outputDirectory, "alldata.bin");
        var outputManifest = System.IO.Path.Combine(outputDirectory, "alldata.psb.m");
        if (SamePath(outputBin, binPath) || SamePath(outputManifest, manifestPath))
            throw new ArgumentException("The output must not overwrite the source archive.", nameof(outputDirectory));
        Directory.CreateDirectory(outputDirectory);

        var layout = new List<AllDataEntry>(Entries.Count);
        using (var source = File.OpenRead(binPath))
        using (var output = File.Create(outputBin))
        {
            long cursor = 0;
            var buffer = new byte[81920];
            foreach (var entry in Entries)
            {
                long length;
                if (replacements.TryGetValue(entry.Path, out var replacement))
                {
                    progress?.Report($"Replacing {entry.Path}");
                    output.Write(replacement);
                    length = replacement.Length;
                }
                else
                {
                    source.Seek(entry.Offset, SeekOrigin.Begin);
                    CopyExactly(source, output, entry.Length, buffer);
                    length = entry.Length;
                }

                layout.Add(new AllDataEntry(entry.Path, cursor, length));
                var end = cursor + length;
                var next = (end + Alignment - 1) / Alignment * Alignment;
                WriteZeros(output, next - end, buffer);
                cursor = next;
            }
        }

        var rewritten = !layout.SequenceEqual(Entries);
        File.WriteAllBytes(outputManifest, rewritten ? BuildManifest(layout) : manifestFile);
        return new AllDataBuildResult(replacements.Count, new FileInfo(outputBin).Length, rewritten);
    }

    byte[] BuildManifest(IReadOnlyList<AllDataEntry> layout)
    {
        var root = (JObject)manifest.DeepClone();
        var info = (JObject)root["file_info"]!;
        foreach (var entry in layout)
            info[entry.Path] = new JArray(entry.Offset, entry.Length);

        using var psb = new MemoryStream();
        new PsbWriter(root, null) { Version = ManifestPsbVersion }.Write(psb);

        var name = System.IO.Path.GetFileName(manifestPath);
        return manifestPath.EndsWith(".m", StringComparison.OrdinalIgnoreCase)
            ? archive.Compress(name, psb.ToArray())
            : psb.ToArray();
    }

    AllDataEntry Find(string path) =>
        Entries.FirstOrDefault(e => e.Path == path)
        ?? throw new KeyNotFoundException($"The archive has no entry '{path}'.");

    static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    static void CopyExactly(Stream source, Stream destination, long length, byte[] buffer)
    {
        while (length > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0) throw new EndOfStreamException("The archive is shorter than its manifest says.");
            destination.Write(buffer, 0, read);
            length -= read;
        }
    }

    static void WriteZeros(Stream destination, long count, byte[] buffer)
    {
        Array.Clear(buffer);
        while (count > 0)
        {
            var chunk = (int)Math.Min(buffer.Length, count);
            destination.Write(buffer, 0, chunk);
            count -= chunk;
        }
    }
}

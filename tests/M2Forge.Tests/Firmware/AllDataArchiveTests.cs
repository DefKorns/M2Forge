using GMWare.M2.MArchive;
using M2Forge.Core.Firmware;

namespace M2Forge.Tests.Firmware;

public class AllDataArchiveTests : IDisposable
{
    const string Seed = "seed-for-tests";

    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-ada-" + Guid.NewGuid().ToString("N"));
    readonly MArchiveBytes bytes = new(ConsoleProfile.MegaDriveMini, Seed);

    string Source => Path.Combine(work, "source");

    AllDataArchive CreateSyntheticArchive()
    {
        var content = Path.Combine(work, "content");
        Directory.CreateDirectory(Path.Combine(content, "dir"));
        File.WriteAllBytes(Path.Combine(content, "one.psb"), Enumerable.Range(0, 3000).Select(i => (byte)(i % 7)).ToArray());
        File.WriteAllBytes(Path.Combine(content, "dir", "two.bin"), Enumerable.Range(0, 5000).Select(i => (byte)(i % 13)).ToArray());
        File.WriteAllBytes(Path.Combine(content, "three.txt"), "hello"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(content, "sound"));
        File.WriteAllBytes(Path.Combine(content, "sound", "raw.bin"), [9, 9, 9]);

        Directory.CreateDirectory(Source);
        var packer = new MArchivePacker(new ZstdSharpCodec(), Seed, 64);
        packer.CompressDirectory(content);
        AllDataPacker.Build(content, Path.Combine(Source, "alldata"), packer);

        return AllDataArchive.Open(Path.Combine(Source, "alldata.psb.m"), Path.Combine(Source, "alldata.bin"), bytes);
    }

    [Fact]
    public void Open_ListsEntriesInOffsetOrder()
    {
        var archive = CreateSyntheticArchive();

        Assert.Equal(
            ["dir/two.bin.m", "one.psb.m", "sound/raw.bin", "three.txt.m"],
            archive.Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
        Assert.Equal(archive.Entries.OrderBy(e => e.Offset), archive.Entries);
        Assert.All(archive.Entries, e => Assert.Equal(0, e.Offset % AllDataArchive.Alignment));
        Assert.Equal(new byte[] { 9, 9, 9 }, archive.ReadStored("sound/raw.bin"));
    }

    [Fact]
    public void Build_WithoutReplacements_ReproducesBothFilesExactly()
    {
        var archive = CreateSyntheticArchive();

        var result = archive.Build(Path.Combine(work, "out"));

        Assert.False(result.ManifestRewritten);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Source, "alldata.bin")), File.ReadAllBytes(Path.Combine(work, "out", "alldata.bin")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Source, "alldata.psb.m")), File.ReadAllBytes(Path.Combine(work, "out", "alldata.psb.m")));
    }

    [Fact]
    public void Build_WithGrowingReplacement_RepacksAndRewritesTheManifest()
    {
        var archive = CreateSyntheticArchive();
        var untouched = archive.Entries.Where(e => e.Path != "one.psb.m").ToDictionary(e => e.Path, e => archive.ReadStored(e.Path));
        var bigger = new byte[7000];
        new Random(4).NextBytes(bigger);

        var result = archive.Build(Path.Combine(work, "out"), new Dictionary<string, byte[]> { ["one.psb.m"] = bigger });

        Assert.True(result.ManifestRewritten);
        var rebuilt = AllDataArchive.Open(Path.Combine(work, "out", "alldata.psb.m"), Path.Combine(work, "out", "alldata.bin"), bytes);
        Assert.Equal(archive.Entries.Select(e => e.Path), rebuilt.Entries.Select(e => e.Path));
        Assert.All(rebuilt.Entries, e => Assert.Equal(0, e.Offset % AllDataArchive.Alignment));
        Assert.Equal(bigger, rebuilt.ReadStored("one.psb.m"));
        foreach (var (path, stored) in untouched)
            Assert.Equal(stored, rebuilt.ReadStored(path));
        Assert.Equal(result.BinLength, new FileInfo(Path.Combine(work, "out", "alldata.bin")).Length);
    }

    [Fact]
    public void Build_WithShrinkingReplacement_Works()
    {
        var archive = CreateSyntheticArchive();

        archive.Build(Path.Combine(work, "out"), new Dictionary<string, byte[]> { ["dir/two.bin.m"] = [1, 2, 3] });

        var rebuilt = AllDataArchive.Open(Path.Combine(work, "out", "alldata.psb.m"), Path.Combine(work, "out", "alldata.bin"), bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, rebuilt.ReadStored("dir/two.bin.m"));
    }

    [Fact]
    public void ReadDecompressed_ReturnsTheOriginalContent()
    {
        var archive = CreateSyntheticArchive();

        Assert.Equal(Enumerable.Range(0, 3000).Select(i => (byte)(i % 7)).ToArray(), archive.ReadDecompressed("one.psb.m"));
        Assert.Equal("hello"u8.ToArray(), archive.ReadDecompressed("three.txt.m"));
        Assert.Equal(new byte[] { 9, 9, 9 }, archive.ReadDecompressed("sound/raw.bin"));
    }

    [Fact]
    public void Build_RefusesToOverwriteTheSource()
    {
        var archive = CreateSyntheticArchive();
        Assert.Throws<ArgumentException>(() => archive.Build(Source));
    }

    [Fact]
    public void Build_RejectsUnknownEntries()
    {
        var archive = CreateSyntheticArchive();
        Assert.Throws<KeyNotFoundException>(() =>
            archive.Build(Path.Combine(work, "out"), new Dictionary<string, byte[]> { ["missing.m"] = [1] }));
        Assert.False(Directory.Exists(Path.Combine(work, "out")));
    }

    [Fact]
    public void Open_RejectsAManifestThatIsNotAnArchive()
    {
        Directory.CreateDirectory(Source);
        var path = Path.Combine(Source, "alldata.psb");
        var root = Newtonsoft.Json.Linq.JToken.Parse("{\"id\":\"motion\"}");
        using (var file = File.Create(path))
            new GMWare.M2.Psb.PsbWriter(root, null) { Version = 3 }.Write(file);
        File.WriteAllBytes(Path.Combine(Source, "alldata.bin"), []);

        Assert.Throws<InvalidDataException>(() => AllDataArchive.Open(path, Path.Combine(Source, "alldata.bin"), bytes));
    }

    [FirmwareFact]
    public void FirmwareRebuild_WithoutChanges_IsByteIdentical()
    {
        var folder = TestSupport.FirmwareFolder();
        var seed = PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage"))!;
        var archive = AllDataArchive.Open(
            Path.Combine(folder, "alldata.psb.m"),
            Path.Combine(folder, "alldata.bin"),
            new MArchiveBytes(ConsoleProfile.MegaDriveMini, seed));

        var result = archive.Build(Path.Combine(work, "out"));

        Assert.False(result.ManifestRewritten);
        Assert.Equal(323, archive.Entries.Count);
        Assert.Equal(TestSupport.Sha256(Path.Combine(folder, "alldata.bin")), TestSupport.Sha256(Path.Combine(work, "out", "alldata.bin")));
        Assert.Equal(TestSupport.Sha256(Path.Combine(folder, "alldata.psb.m")), TestSupport.Sha256(Path.Combine(work, "out", "alldata.psb.m")));
    }

    [FirmwareFact]
    public void FirmwareRebuild_WithRecompressedEntry_ExtractsToTheSameFiles()
    {
        var folder = TestSupport.FirmwareFolder();
        var reference = TestSupport.ReferenceFolder();
        var seed = PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage"))!;
        var archive = AllDataArchive.Open(
            Path.Combine(folder, "alldata.psb.m"),
            Path.Combine(folder, "alldata.bin"),
            new MArchiveBytes(ConsoleProfile.MegaDriveMini, seed));

        const string target = "032/motion/eu_titleselect_eu.psb.m";
        var content = archive.ReadDecompressed(target);
        var recompressed = new MArchiveBytes(ConsoleProfile.MegaDriveMini, seed).Compress("eu_titleselect_eu.psb.m", content);
        var original = archive.ReadStored(target);
        Assert.NotEqual(original.Length, recompressed.Length);

        var rebuilt = Path.Combine(work, "rebuilt");
        var result = archive.Build(rebuilt, new Dictionary<string, byte[]> { [target] = recompressed });
        Assert.True(result.ManifestRewritten);

        var reopened = AllDataArchive.Open(
            Path.Combine(rebuilt, "alldata.psb.m"),
            Path.Combine(rebuilt, "alldata.bin"),
            new MArchiveBytes(ConsoleProfile.MegaDriveMini, seed));
        Assert.Equal(archive.Entries.Select(e => e.Path), reopened.Entries.Select(e => e.Path));
        foreach (var entry in archive.Entries.Where(e => e.Path != target))
            Assert.True(archive.ReadStored(entry.Path).AsSpan().SequenceEqual(reopened.ReadStored(entry.Path)), entry.Path);

        var ours = Path.Combine(work, "ours");
        new FirmwareExtractor(ConsoleProfile.MegaDriveMini, seed).Extract(Path.Combine(rebuilt, "alldata.psb.m"), ours);
        TestSupport.AssertSameTree(reference, ours);

        var batchDir = Path.Combine(work, "batch");
        Directory.CreateDirectory(batchDir);
        File.Copy(Path.Combine(rebuilt, "alldata.bin"), Path.Combine(batchDir, "alldata.bin"));
        File.Copy(Path.Combine(rebuilt, "alldata.psb.m"), Path.Combine(batchDir, "alldata.psb.m"));
        TestSupport.RunBatchFullUnpack(batchDir, seed);
        TestSupport.AssertSameTree(reference, Path.Combine(batchDir, "alldata.psb.m_extracted"));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

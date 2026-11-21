using GMWare.M2.MArchive;
using GMWare.M2.Psb;
using M2Forge.Core.Firmware;
using M2Forge.Core.Projects;
using M2Forge.Core.Psb;
using Newtonsoft.Json.Linq;

namespace M2Forge.Tests.Firmware;

public sealed class TreePackerTests : IDisposable
{
    const string Seed = "seed-for-tests";

    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-tree-" + Guid.NewGuid().ToString("N"));
    readonly MArchiveBytes codec = new(ConsoleProfile.MegaDriveMini, Seed);

    string Source => Path.Combine(work, "source");
    string Extracted => Path.Combine(work, "extracted");

    static byte[] ValidPsb()
    {
        using var stream = new MemoryStream();
        new PsbWriter(new JObject { ["name"] = "test", ["value"] = 3 }, null) { Version = 3 }.Write(stream);
        return stream.ToArray();
    }

    AllDataArchive CreateArchiveAndTree()
    {
        var content = Path.Combine(work, "content");
        Directory.CreateDirectory(Path.Combine(content, "dir"));
        File.WriteAllBytes(Path.Combine(content, "one.psb"), ValidPsb());
        File.WriteAllBytes(Path.Combine(content, "dir", "two.bin"), Enumerable.Range(0, 5000).Select(i => (byte)(i % 13)).ToArray());
        Directory.CreateDirectory(Path.Combine(content, "sound"));
        File.WriteAllBytes(Path.Combine(content, "sound", "raw.bin"), [9, 9, 9]);

        Directory.CreateDirectory(Source);
        var packer = new MArchivePacker(new ZstdSharpCodec(), Seed, 64);
        packer.CompressDirectory(content);
        AllDataPacker.Build(content, Path.Combine(Source, "alldata"), packer);

        var archive = AllDataArchive.Open(Path.Combine(Source, "alldata.psb.m"), Path.Combine(Source, "alldata.bin"), codec);
        foreach (var entry in archive.Entries)
        {
            var target = Path.Combine(Extracted, TreePacker.RelativePath(entry.Path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, archive.ReadDecompressed(entry.Path));
        }
        return archive;
    }

    [Fact]
    public void AnUntouchedTree_HasNoChanges()
    {
        var archive = CreateArchiveAndTree();

        var scan = TreePacker.Scan(archive, codec, Extracted);

        Assert.Empty(scan.Changes);
        Assert.Empty(scan.Replacements);
        Assert.Empty(scan.Issues);
        Assert.Empty(scan.Warnings);
        Assert.Equal(3, scan.Compared);
    }

    [Fact]
    public void AnEditedFile_IsRecompressedAndRoundTrips()
    {
        var archive = CreateArchiveAndTree();
        var edited = File.ReadAllBytes(Path.Combine(Extracted, "dir", "two.bin"));
        edited[10] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(Extracted, "dir", "two.bin"), edited);

        var scan = TreePacker.Scan(archive, codec, Extracted);

        var change = Assert.Single(scan.Changes);
        Assert.Equal("dir/two.bin.m", change.Entry);
        Assert.Equal("dir/two.bin", change.RelativePath);
        Assert.Equal(edited, codec.Decompress("two.bin.m", scan.Replacements["dir/two.bin.m"]));
    }

    [Fact]
    public void AnEditedRawEntry_IsStoredAsIs()
    {
        var archive = CreateArchiveAndTree();
        File.WriteAllBytes(Path.Combine(Extracted, "sound", "raw.bin"), [1, 2, 3, 4]);

        var scan = TreePacker.Scan(archive, codec, Extracted);

        Assert.Equal("sound/raw.bin", Assert.Single(scan.Changes).Entry);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, scan.Replacements["sound/raw.bin"]);
    }

    [Fact]
    public void AMissingFile_KeepsTheOriginalAndWarns()
    {
        var archive = CreateArchiveAndTree();
        File.Delete(Path.Combine(Extracted, "one.psb"));

        var scan = TreePacker.Scan(archive, codec, Extracted);

        Assert.Empty(scan.Changes);
        Assert.Equal(1, scan.Missing);
        Assert.Contains("1 file(s)", Assert.Single(scan.Warnings));
    }

    [Fact]
    public void AJsonThatMatchesItsPsb_IsSilentEvenWhenWrittenLater()
    {
        var archive = CreateArchiveAndTree();
        var json = Path.Combine(Extracted, "one.psb.json");
        File.WriteAllText(json, PsbDump.ToJson(Path.Combine(Extracted, "one.psb")));
        File.SetLastWriteTimeUtc(json, File.GetLastWriteTimeUtc(Path.Combine(Extracted, "one.psb")).AddMinutes(5));

        var scan = TreePacker.Scan(archive, codec, Extracted);

        Assert.Empty(scan.Changes);
        Assert.Empty(scan.Warnings);
    }

    [Fact]
    public void AnEditedJson_WarnsThatOnlyThePsbIsPacked()
    {
        var archive = CreateArchiveAndTree();
        var json = Path.Combine(Extracted, "one.psb.json");
        File.WriteAllText(json, PsbDump.ToJson(Path.Combine(Extracted, "one.psb")).Replace("test", "edited"));

        var scan = TreePacker.Scan(archive, codec, Extracted);

        Assert.Empty(scan.Changes);
        Assert.Contains("one.psb.json", Assert.Single(scan.Warnings));
    }

    [Fact]
    public void ProjectPlan_PacksTheChangedFilesAndTheRebuildReadsBack()
    {
        var archive = CreateArchiveAndTree();
        var edited = File.ReadAllBytes(Path.Combine(Extracted, "one.psb"));
        edited[0] = 0x42;
        File.WriteAllBytes(Path.Combine(Extracted, "one.psb"), edited);
        var project = new EditProject { Console = ConsoleKind.MegaDriveMini, Firmware = Source, Seed = Seed, Extracted = Extracted };

        var plan = ProjectBuilder.Plan(project);
        var result = ProjectBuilder.Build(project, plan, Path.Combine(work, "out"));

        Assert.True(plan.CanBuild);
        Assert.Equal("one.psb", Assert.Single(plan.Tree).RelativePath);
        Assert.Equal(1, result.Archive!.Replaced);

        var rebuilt = AllDataArchive.Open(Path.Combine(work, "out", "alldata.psb.m"), Path.Combine(work, "out", "alldata.bin"), codec);
        Assert.Equal(edited, rebuilt.ReadDecompressed("one.psb.m"));
        Assert.Equal(archive.ReadDecompressed("dir/two.bin.m"), rebuilt.ReadDecompressed("dir/two.bin.m"));
    }

    [Fact]
    public void ProjectPlan_RejectsATextureEditAndATreeChangeOnTheSameFile()
    {
        CreateArchiveAndTree();
        File.WriteAllBytes(Path.Combine(Extracted, "one.psb"), [7, 7, 7]);
        var project = new EditProject
        {
            Console = ConsoleKind.MegaDriveMini,
            Firmware = Source,
            Seed = Seed,
            Extracted = Extracted,
            Edits = [new TextureEdit("one.psb", "tex", null, "missing.png")],
        };

        var plan = ProjectBuilder.Plan(project);

        Assert.False(plan.CanBuild);
    }

    [FirmwareFact]
    public void TheExtractedMini1Tree_MatchesItsFirmwareExactly()
    {
        var folder = TestSupport.FirmwareFolder();
        var seed = PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage"))!;
        var mini1 = new MArchiveBytes(ConsoleProfile.MegaDriveMini, seed);
        var archive = AllDataArchive.Open(Path.Combine(folder, "alldata.psb.m"), Path.Combine(folder, "alldata.bin"), mini1);

        var scan = TreePacker.Scan(archive, mini1, TestSupport.ReferenceFolder());

        Assert.Empty(scan.Issues);
        Assert.Empty(scan.Changes);
        Assert.Equal(archive.Entries.Count, scan.Compared);
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

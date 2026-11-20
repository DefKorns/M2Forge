using GMWare.M2.MArchive;
using GMWare.M2.Psb;
using M2Forge.Core.Firmware;
using M2Forge.Core.Projects;
using M2Forge.Core.Psb;
using M2Forge.Core.Textures;
using Newtonsoft.Json.Linq;

namespace M2Forge.Tests.Projects;

public class ProjectBuilderTests : IDisposable
{
    const string Seed = "seed-for-tests";
    const string Psb = "sys/motion/m.psb";

    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-pb-" + Guid.NewGuid().ToString("N"));
    readonly MArchiveBytes codec = new(ConsoleProfile.MegaDriveMini, Seed);

    string Firmware => Path.Combine(work, "firmware");

    static byte[] Pattern(int count, int seed)
    {
        var bytes = new byte[count];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    byte[] OriginalPsb { get; set; } = [];

    void CreateFirmware(bool withEngine = true)
    {
        var root = new JObject
        {
            ["id"] = "motion",
            ["source"] = new JObject
            {
                ["tex"] = new JObject
                {
                    ["texture"] = new JObject
                    {
                        ["type"] = "RGBA8",
                        ["width"] = 4,
                        ["height"] = 4,
                        ["pixel"] = JStream.FromBytes(Pattern(64, 1), false),
                    },
                    ["icon"] = new JObject { ["i"] = new JObject { ["left"] = 1, ["top"] = 1, ["width"] = 2, ["height"] = 2 } },
                },
                ["mask"] = new JObject
                {
                    ["texture"] = new JObject
                    {
                        ["type"] = "A8",
                        ["width"] = 4,
                        ["height"] = 4,
                        ["pixel"] = JStream.FromBytes(Pattern(16, 2), false),
                    },
                },
            },
        };

        var content = Path.Combine(work, "content");
        Directory.CreateDirectory(Path.Combine(content, "sys", "motion"));
        Directory.CreateDirectory(Path.Combine(content, "sound"));
        using (var file = File.Create(Path.Combine(content, "sys", "motion", "m.psb")))
            new PsbWriter(root, null) { Version = 4, Optimize = false }.Write(file);
        OriginalPsb = File.ReadAllBytes(Path.Combine(content, "sys", "motion", "m.psb"));
        File.WriteAllBytes(Path.Combine(content, "sys", "motion", "other.nut"), Pattern(3000, 3));
        File.WriteAllBytes(Path.Combine(content, "sound", "raw.bin"), Pattern(500, 4));

        Directory.CreateDirectory(Firmware);
        var packer = new MArchivePacker(new ZstdSharpCodec(), Seed, 64);
        packer.CompressDirectory(content);
        AllDataPacker.Build(content, Path.Combine(Firmware, "alldata"), packer);

        if (withEngine)
            File.WriteAllBytes(
                Path.Combine(Firmware, "m2engage"),
                [.. "junk\0"u8.ToArray(), .. "getExistFileDirInMountArchive\0\0\0"u8.ToArray(), .. System.Text.Encoding.ASCII.GetBytes(Seed), 0, 1, 2]);
    }

    string Png(string name, int width, int height, Func<int, byte[]> pixel)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++) pixel(i).CopyTo(pixels, i * 4);
        var path = Path.Combine(work, "edits", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PngEncoder.Save(new RgbaImage(width, height, pixels), path);
        return path;
    }

    EditProject Project(params TextureEdit[] edits) => new()
    {
        Firmware = Firmware,
        Edits = [.. edits],
        BaseDirectory = work,
    };

    AllDataArchive Reopen(string folder) =>
        AllDataArchive.Open(Path.Combine(folder, "alldata.psb.m"), Path.Combine(folder, "alldata.bin"), codec);

    [Fact]
    public void Build_PatchesOnlyTheIconRegionInsideThePsb()
    {
        CreateFirmware();
        var red = Png("red.png", 2, 2, _ => [255, 0, 0, 255]);
        var output = Path.Combine(work, "out");

        var result = ProjectBuilder.Build(Project(new TextureEdit(Psb, "tex", "i", red)), output);

        Assert.True(result.Plan.CanBuild);
        Assert.Equal(1, result.Plan.EditsApplied);
        Assert.Empty(result.Plan.Warnings);
        Assert.NotNull(result.Archive);

        var rebuilt = Reopen(output);
        var patched = rebuilt.ReadDecompressed(Psb + ".m");
        Assert.Equal(OriginalPsb.Length, patched.Length);

        var stream = new PsbStreamEditor(OriginalPsb).Streams.Single(s => s.Length == 64);
        var changed = Enumerable.Range(0, patched.Length).Where(i => patched[i] != OriginalPsb[i]).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, i => Assert.InRange(i, stream.Offset, stream.Offset + stream.Length - 1));

        var pixels = new PsbStreamEditor(patched).Read(stream.Kind, stream.Index).ToArray();
        var original = new PsbStreamEditor(OriginalPsb).Read(stream.Kind, stream.Index).ToArray();
        for (var p = 0; p < 16; p++)
        {
            var inside = p % 4 is 1 or 2 && p / 4 is 1 or 2;
            var expected = inside ? new byte[] { 255, 0, 0, 255 } : original[(p * 4)..(p * 4 + 4)];
            Assert.Equal(expected, pixels[(p * 4)..(p * 4 + 4)]);
        }
    }

    [Fact]
    public void Build_LeavesEveryOtherEntryUntouched()
    {
        CreateFirmware();
        var source = Reopen(Firmware);
        var red = Png("red.png", 2, 2, _ => [255, 0, 0, 255]);
        var output = Path.Combine(work, "out");

        ProjectBuilder.Build(Project(new TextureEdit(Psb, "tex", "i", red)), output);

        var rebuilt = Reopen(output);
        Assert.Equal(source.Entries.Select(e => e.Path), rebuilt.Entries.Select(e => e.Path).OrderBy(p => source.Entries.ToList().FindIndex(e => e.Path == p)));
        foreach (var entry in source.Entries.Where(e => e.Path != Psb + ".m"))
            Assert.Equal(source.ReadStored(entry.Path), rebuilt.ReadStored(entry.Path));
    }

    [Fact]
    public void Build_AppliesSeveralEditsToOneFileInASingleEntry()
    {
        CreateFirmware();
        var red = Png("red.png", 2, 2, _ => [255, 0, 0, 255]);
        var mask = Png("mask.png", 4, 4, i => [0, 0, 0, (byte)(i * 10)]);
        var output = Path.Combine(work, "out");

        var result = ProjectBuilder.Build(
            Project(new TextureEdit(Psb, "tex", "i", red), new TextureEdit(Psb, "mask", null, mask)),
            output);

        Assert.Equal(2, result.Plan.EditsApplied);
        Assert.Single(result.Plan.Replacements);

        var patched = Reopen(output).ReadDecompressed(Psb + ".m");
        var editor = new PsbStreamEditor(patched);
        var maskStream = editor.Streams.Single(s => s.Length == 16);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)(i * 10)).ToArray(), editor.Read(maskStream.Kind, maskStream.Index).ToArray());
    }

    [Fact]
    public void Plan_WarnsAboutLossyFormats()
    {
        CreateFirmware();
        var opaque = Png("opaque.png", 4, 4, _ => [10, 20, 30, 255]);

        var plan = ProjectBuilder.Plan(Project(new TextureEdit(Psb, "mask", null, opaque)));

        Assert.True(plan.CanBuild);
        var warning = Assert.Single(plan.Warnings);
        Assert.Contains("solid block", warning);
        Assert.Contains("mask", warning);
    }

    [Fact]
    public void Plan_ReportsEveryProblemAndBuildsNothing()
    {
        CreateFirmware();
        var small = Png("small.png", 3, 3, _ => [1, 2, 3, 4]);
        var good = Png("good.png", 2, 2, _ => [1, 2, 3, 4]);
        var interlaced = Path.Combine(work, "edits", "interlaced.png");
        File.WriteAllBytes(interlaced, Textures.PngBuilder.Build(1, 1, 8, 6, [[1, 2, 3, 4]], interlace: 1));

        var project = Project(
            new TextureEdit("missing/file.psb", "tex", null, good),
            new TextureEdit(Psb, "nothing", null, good),
            new TextureEdit(Psb, "tex", "nope", good),
            new TextureEdit(Psb, "tex", "i", Path.Combine(work, "edits", "absent.png")),
            new TextureEdit(Psb, "tex", "i", small),
            new TextureEdit(Psb, "tex", "i", interlaced));
        var output = Path.Combine(work, "out");

        var result = ProjectBuilder.Build(project, output);

        Assert.False(result.Plan.CanBuild);
        Assert.Null(result.Archive);
        Assert.Empty(result.Plan.Replacements);
        Assert.Equal(6, result.Plan.Issues.Count);
        Assert.Contains(result.Plan.Issues, m => m.Contains("not in the firmware archive"));
        Assert.Contains(result.Plan.Issues, m => m.Contains("no texture 'nothing'"));
        Assert.Contains(result.Plan.Issues, m => m.Contains("no icon 'nope'"));
        Assert.Contains(result.Plan.Issues, m => m.Contains("cannot read image"));
        Assert.Contains(result.Plan.Issues, m => m.Contains("3x3") && m.Contains("'i'"));
        Assert.Contains(result.Plan.Issues, m => m.Contains("nterlac"));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void Build_WithoutEdits_ReproducesTheArchiveExactly()
    {
        CreateFirmware();
        var output = Path.Combine(work, "out");

        var result = ProjectBuilder.Build(Project(), output);

        Assert.True(result.Plan.CanBuild);
        Assert.False(result.Archive!.ManifestRewritten);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Firmware, "alldata.bin")), File.ReadAllBytes(Path.Combine(output, "alldata.bin")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Firmware, "alldata.psb.m")), File.ReadAllBytes(Path.Combine(output, "alldata.psb.m")));
    }

    [Fact]
    public void Build_NeedsASeed()
    {
        CreateFirmware(withEngine: false);

        Assert.Throws<InvalidOperationException>(() => ProjectBuilder.Plan(Project()));

        var project = Project();
        project.Seed = Seed;
        Assert.True(ProjectBuilder.Plan(project).CanBuild);
    }

    [FirmwareFact]
    public void FirmwareBuild_ChangesOnlyTheEditedIcon()
    {
        var folder = TestSupport.FirmwareFolder();
        var reference = TestSupport.ReferenceFolder();
        var seed = PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage"))!;

        const string psb = "system/motion/titleselect_ui.psb";
        var sources = TextureCatalog.Read(Path.Combine(reference, "system", "motion", "titleselect_ui.psb.json"));
        var source = sources.Single(s => s.Name == "tex#001");
        var icon = source.Icons.Single(i => i.Name == "0005");

        var current = source.Load().Crop(icon.Left, icon.Top, icon.Width, icon.Height);
        var inverted = new byte[current.Pixels.Length];
        for (var i = 0; i < inverted.Length; i += 4)
        {
            inverted[i] = (byte)(255 - current.Pixels[i]);
            inverted[i + 1] = (byte)(255 - current.Pixels[i + 1]);
            inverted[i + 2] = (byte)(255 - current.Pixels[i + 2]);
            inverted[i + 3] = current.Pixels[i + 3];
        }
        var edited = new RgbaImage(icon.Width, icon.Height, inverted);
        var pngPath = Path.Combine(work, "edits", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        PngEncoder.Save(edited, pngPath);

        var project = new EditProject { Firmware = folder, BaseDirectory = work, Edits = [new TextureEdit(psb, "tex#001", "0005", pngPath)] };
        var rebuilt = Path.Combine(work, "rebuilt");
        var result = ProjectBuilder.Build(project, rebuilt);
        Assert.True(result.Plan.CanBuild, string.Join("; ", result.Plan.Issues));

        var ours = Path.Combine(work, "ours");
        new FirmwareExtractor(ConsoleProfile.MegaDriveMini, seed).Extract(Path.Combine(rebuilt, "alldata.psb.m"), ours);

        var expected = TestSupport.Hashes(reference);
        var actual = TestSupport.Hashes(ours);
        Assert.Empty(expected.Keys.Except(actual.Keys));
        Assert.Empty(actual.Keys.Except(expected.Keys));
        var differing = expected.Where(kv => actual[kv.Key] != kv.Value).Select(kv => kv.Key).OrderBy(k => k).ToList();
        Assert.Equal(2, differing.Count);
        Assert.Contains("system/motion/titleselect_ui.psb", differing);
        Assert.Contains(differing, k => k.StartsWith("system/motion/titleselect_ui.psb.streams/"));

        var streamName = $"stream_{source.StreamIndex}";
        var before = File.ReadAllBytes(Path.Combine(reference, "system", "motion", "titleselect_ui.psb.streams", streamName));
        var after = File.ReadAllBytes(Path.Combine(ours, "system", "motion", "titleselect_ui.psb.streams", streamName));
        Assert.Equal(before.Length, after.Length);
        var changedPixels = Enumerable.Range(0, before.Length / 4).Where(p => !before.AsSpan(p * 4, 4).SequenceEqual(after.AsSpan(p * 4, 4))).ToList();
        Assert.NotEmpty(changedPixels);
        Assert.All(changedPixels, p =>
        {
            Assert.InRange(p % source.Width, icon.Left, icon.Left + icon.Width - 1);
            Assert.InRange(p / source.Width, icon.Top, icon.Top + icon.Height - 1);
        });

        var shown = TextureCatalog.Read(Path.Combine(ours, "system", "motion", "titleselect_ui.psb.json")).Single(s => s.Name == "tex#001");
        Assert.Equal(inverted, shown.Load().Crop(icon.Left, icon.Top, icon.Width, icon.Height).Pixels);

        var batchDir = Path.Combine(work, "batch");
        Directory.CreateDirectory(batchDir);
        File.Copy(Path.Combine(rebuilt, "alldata.bin"), Path.Combine(batchDir, "alldata.bin"));
        File.Copy(Path.Combine(rebuilt, "alldata.psb.m"), Path.Combine(batchDir, "alldata.psb.m"));
        TestSupport.RunBatchFullUnpack(batchDir, seed);
        TestSupport.AssertSameTree(ours, Path.Combine(batchDir, "alldata.psb.m_extracted"));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

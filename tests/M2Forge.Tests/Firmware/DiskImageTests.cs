using System.Buffers.Binary;
using M2Forge.Core.Firmware;
using M2Forge.Core.Roms;

namespace M2Forge.Tests.Firmware;

public sealed class DiskImageFactAttribute : FactAttribute
{
    public const string Variable = "M2FORGE_TEST_MD2_IMAGE";

    public DiskImageFactAttribute()
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            Skip = $"Set {Variable} to a Mega Drive Mini 2 eMMC image.";
    }
}

public class DiskImageTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-disk-" + Guid.NewGuid().ToString("N"));

    static void SetEntry(byte[] sector, int index, byte type, uint start, uint count)
    {
        var entry = sector.AsSpan(446 + index * 16, 16);
        entry[4] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], start);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], count);
    }

    string Image(Action<byte[]> build)
    {
        Directory.CreateDirectory(work);
        var bytes = new byte[300 * 512];
        build(bytes);
        var path = Path.Combine(work, "disk.img");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    static void Sign(byte[] disk, int sector)
    {
        disk[sector * 512 + 510] = 0x55;
        disk[sector * 512 + 511] = 0xAA;
    }

    [Fact]
    public void Partitions_ReadPrimaryAndLogicalEntries()
    {
        var path = Image(disk =>
        {
            var mbr = new byte[512];
            SetEntry(mbr, 0, 0x0B, 100, 50);
            SetEntry(mbr, 1, 0x05, 200, 90);
            mbr.CopyTo(disk, 0);
            Sign(disk, 0);

            var first = new byte[512];
            SetEntry(first, 0, 0x83, 2, 10);
            SetEntry(first, 1, 0x05, 20, 20);
            first.CopyTo(disk, 200 * 512);
            Sign(disk, 200);

            var second = new byte[512];
            SetEntry(second, 0, 0x83, 2, 8);
            second.CopyTo(disk, 220 * 512);
            Sign(disk, 220);
        });

        var partitions = DiskPartitions.Read(path);

        Assert.Equal([1, 5, 6], partitions.Select(p => p.Number));
        Assert.Equal(100 * 512L, partitions[0].Offset);
        Assert.Equal(202 * 512L, partitions[1].Offset);
        Assert.Equal(10 * 512L, partitions[1].Length);
        Assert.Equal(222 * 512L, partitions[2].Offset);
        Assert.All(partitions.Skip(1), p => Assert.Equal(0x83, p.Type));
    }

    [Fact]
    public void Partitions_RejectAnImageWithoutAnMbr()
    {
        var path = Image(_ => { });

        Assert.Throws<InvalidDataException>(() => DiskPartitions.Read(path));
    }

    [Fact]
    public void ImportDiskImage_FailsWhenNoPartitionHoldsTheFirmware()
    {
        var path = Image(disk =>
        {
            var mbr = new byte[512];
            SetEntry(mbr, 0, 0x83, 10, 100);
            mbr.CopyTo(disk, 0);
            Sign(disk, 0);
        });

        Assert.Throws<InvalidDataException>(() => FirmwareImporter.ImportDiskImage(path, Path.Combine(work, "out")));
    }

    [Fact]
    public void DetectDiskImage_ReturnsNullWhenNoPartitionHoldsTheFirmware()
    {
        var path = Image(disk =>
        {
            var mbr = new byte[512];
            SetEntry(mbr, 0, 0x83, 10, 100);
            mbr.CopyTo(disk, 0);
            Sign(disk, 0);
        });

        Assert.Null(FirmwareImporter.DetectDiskImage(path));
    }

    [Fact]
    public void KeyByHash_IgnoresBytesThatAreNotTheKey()
    {
        var noise = new byte[4096];
        new Random(7).NextBytes(noise);

        Assert.Null(PsbKeyFinder.FindByHash(noise));
    }

    [FirmwareFact]
    public void KeyByHash_AgreesWithTheMarkerOnTheMini1Engine()
    {
        var data = File.ReadAllBytes(Path.Combine(TestSupport.FirmwareFolder(), "m2engage"));

        var byHash = PsbKeyFinder.FindByHash(data);

        Assert.NotNull(byHash);
        Assert.Equal(PsbKeyFinder.FindByMarker(data), byHash);
    }

    [DiskImageFact]
    public void TheMini2Image_UnpacksToTheCatalogedFilesAndExtractsWithoutFailures()
    {
        var image = Environment.GetEnvironmentVariable(DiskImageFactAttribute.Variable)!;
        var files = Path.Combine(work, "files");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(ConsoleKind.MegaDriveMini2, FirmwareImporter.DetectDiskImage(image));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "detecting the console must not copy the firmware");

        var result = FirmwareImporter.ImportDiskImage(image, files);
        var report = FirmwareValidator.Validate(files);

        Assert.Equal(FirmwareImporter.Wanted.Order(), result.Files.Order());
        Assert.Equal(ConsoleKind.MegaDriveMini2, result.Console);
        Assert.True(report.IsGenuine);
        Assert.Equal(ConsoleKind.MegaDriveMini2, report.Match!.Console);

        var seed = PsbKeyFinder.FindInFile(Path.Combine(files, "m2engage"));
        var extracted = new FirmwareExtractor(ConsoleProfile.MegaDriveMini2, seed!)
            .Extract(Path.Combine(files, "alldata.psb.m"), Path.Combine(work, "extracted"));

        Assert.Empty(extracted.Failures);
        Assert.True(extracted.ArchivesDecompressed > 300);

        var tree = Path.Combine(work, "extracted");
        var catalog = RomCatalog.Load(tree);
        Assert.True(catalog.Count > 100);
        Assert.Equal("Streets of Rage 3", catalog.Find("us_us_bare_knuckle_3.bin")!.Title);
        Assert.Equal("ベア・ナックルⅢ", catalog.Find("jp_jp_bare_knuckle_3.bin")!.NativeTitle);

        var artwork = new RomArtwork(catalog.RegionFolder);
        Assert.Equal(["eu", "jp", "us"], artwork.AvailableSets);
        var entry = catalog.Find("us_us_bare_knuckle_3.bin")!.CoverEntry!;
        foreach (var kind in Enum.GetValues<CoverKind>())
        {
            var cover = artwork.Get(entry, kind, "us");
            Assert.NotNull(cover);
            Assert.True(cover.Width > 20 && cover.Height > 100);
        }
        Assert.NotEqual(artwork.Get(entry, CoverKind.Cover, "us")!.Pixels, artwork.Get(entry, CoverKind.Cover, "jp")!.Pixels);

        var withoutCover = RomLibrary.Scan(tree)
            .Where(r => !(catalog.Find(r.FileName)?.CoverEntry is { } e && artwork.Get(e, CoverKind.Cover, artwork.SetForRegion(r.Region)) is not null))
            .Select(r => r.FileName)
            .ToList();
        Assert.All(withoutCover, name => Assert.Contains("bios", name, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

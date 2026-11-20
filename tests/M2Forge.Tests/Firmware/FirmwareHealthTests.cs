using System.Text;
using GMWare.M2.MArchive;
using M2Forge.Core.Firmware;

namespace M2Forge.Tests.Firmware;

public class FirmwareHealthTests : IDisposable
{
    const string Seed = "seed-for-tests";

    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-health-" + Guid.NewGuid().ToString("N"));

    string Folder => Path.Combine(work, "firmware");

    void CreateFirmware()
    {
        var content = Path.Combine(work, "content");
        Directory.CreateDirectory(Path.Combine(content, "dir"));
        File.WriteAllBytes(Path.Combine(content, "one.psb"), Enumerable.Range(0, 3000).Select(i => (byte)(i % 7)).ToArray());
        File.WriteAllBytes(Path.Combine(content, "dir", "two.bin"), Enumerable.Range(0, 5000).Select(i => (byte)(i % 13)).ToArray());

        Directory.CreateDirectory(Folder);
        var packer = new MArchivePacker(new ZstdSharpCodec(), Seed, 64);
        packer.CompressDirectory(content);
        AllDataPacker.Build(content, Path.Combine(Folder, "alldata"), packer);

        File.WriteAllBytes(Path.Combine(Folder, "m2engage"), [.. "x\0getExistFileDirInMountArchive\0\0\0"u8.ToArray(), .. Encoding.ASCII.GetBytes(Seed), 0, 7]);
    }

    [Fact]
    public void ACompleteArchive_HasNoStructuralProblems()
    {
        CreateFirmware();

        var report = FirmwareHealthCheck.CheckStructure(Folder);

        Assert.True(report.Ok, string.Join("; ", report.Problems));
    }

    [Fact]
    public void ACompleteArchiveOfAnUnknownVersion_IsIntactButNotVerified()
    {
        CreateFirmware();

        var health = FirmwareHealthCheck.Assess(Folder);

        Assert.Equal(FirmwareHealth.Intact, health.Health);
        Assert.True(health.IsUsable);
    }

    [Fact]
    public void ATruncatedBin_IsDamaged()
    {
        CreateFirmware();
        var bin = Path.Combine(Folder, "alldata.bin");
        using (var stream = new FileStream(bin, FileMode.Open, FileAccess.Write)) stream.SetLength(new FileInfo(bin).Length / 2);

        var health = FirmwareHealthCheck.Assess(Folder);

        Assert.Equal(FirmwareHealth.Damaged, health.Health);
        Assert.Contains("shorter", health.Detail);
        Assert.False(health.IsUsable);
    }

    [Theory]
    [InlineData("alldata.psb.m")]
    [InlineData("alldata.bin")]
    [InlineData("m2engage")]
    public void AMissingFile_IsDamaged(string name)
    {
        CreateFirmware();
        File.Delete(Path.Combine(Folder, name));

        var health = FirmwareHealthCheck.Assess(Folder);

        Assert.Equal(FirmwareHealth.Damaged, health.Health);
        Assert.Contains(name, health.Detail);
    }

    [Fact]
    public void ACorruptIndex_IsDamaged()
    {
        CreateFirmware();
        File.WriteAllBytes(Path.Combine(Folder, "alldata.psb.m"), new byte[300]);

        Assert.Equal(FirmwareHealth.Damaged, FirmwareHealthCheck.Assess(Folder).Health);
    }

    [Fact]
    public void AnEngineWithoutTheKey_IsDamaged()
    {
        CreateFirmware();
        File.WriteAllBytes(Path.Combine(Folder, "m2engage"), [1, 2, 3, 4]);

        var health = FirmwareHealthCheck.Assess(Folder);

        Assert.Equal(FirmwareHealth.Damaged, health.Health);
        Assert.Contains("key", health.Detail);
    }

    [Fact]
    public void AnEmptyFolder_IsDamaged()
    {
        Directory.CreateDirectory(Folder);

        Assert.Equal(FirmwareHealth.Damaged, FirmwareHealthCheck.Assess(Folder).Health);
    }

    [Fact]
    public void TheCatalogFindsAFirmwareBySizeAlone()
    {
        Assert.Equal(ConsoleKind.MegaDriveMini2, FirmwareCatalog.FindBySize(4755216384)!.Console);
        Assert.Equal("1085EU-1f2365d", FirmwareCatalog.FindBySize(110057472)!.Build);
        Assert.Null(FirmwareCatalog.FindBySize(12345));
        Assert.Null(FirmwareCatalog.FindBySize(0));
    }

    [FirmwareFact]
    public void TheRealMini1Firmware_IsVerified()
    {
        var health = FirmwareHealthCheck.Assess(TestSupport.FirmwareFolder());

        Assert.Equal(FirmwareHealth.Verified, health.Health);
        Assert.NotNull(health.Name);
    }

    [FirmwareFact]
    public void ChangingTheEngine_MakesARealFirmwareModifiedButStillUsable()
    {
        Directory.CreateDirectory(Folder);
        foreach (var name in FirmwareImporter.Wanted)
            File.Copy(Path.Combine(TestSupport.FirmwareFolder(), name), Path.Combine(Folder, name));
        using (var engine = new FileStream(Path.Combine(Folder, "m2engage"), FileMode.Open, FileAccess.Write))
        {
            engine.Position = 100;
            engine.WriteByte(0xAA);
        }

        var health = FirmwareHealthCheck.Assess(Folder);

        Assert.Equal(FirmwareHealth.Modified, health.Health);
        Assert.Contains("m2engage", health.Detail);
        Assert.True(health.IsUsable);
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

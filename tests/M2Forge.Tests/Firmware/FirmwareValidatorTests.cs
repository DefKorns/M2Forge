using M2Forge.Core.Firmware;

namespace M2Forge.Tests.Firmware;

public sealed class FirmwareValidatorTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-val-" + Guid.NewGuid().ToString("N"));

    string Folder(params string[] names)
    {
        Directory.CreateDirectory(work);
        foreach (var name in names) File.WriteAllBytes(Path.Combine(work, name), [1, 2, 3]);
        return work;
    }

    [Fact]
    public void UnknownFiles_AreReportedAsUnknownAndNeverGenuine()
    {
        var report = FirmwareValidator.Validate(Folder("alldata.psb.m", "alldata.bin", "m2engage"));

        Assert.Null(report.Match);
        Assert.False(report.IsGenuine);
        Assert.All(report.Files, f => Assert.Equal(FileVerdict.Unknown, f.Verdict));
    }

    [Fact]
    public void MissingFiles_AreFlagged()
    {
        var report = FirmwareValidator.Validate(Folder("alldata.bin"));

        Assert.True(report.IsMissingFiles);
        Assert.Equal(["alldata.psb.m", "m2engage"], report.Files.Where(f => f.Verdict == FileVerdict.Missing).Select(f => f.Name));
    }

    [Fact]
    public void Catalog_HasDistinctEntries()
    {
        Assert.Equal(5, FirmwareCatalog.All.Count);
        Assert.Equal(5, FirmwareCatalog.All.Select(f => f.BinSha256).Distinct().Count());
        Assert.Equal(4, FirmwareCatalog.All.Select(f => f.ArchiveSha256).Where(h => h.Length > 0).Distinct().Count());
        Assert.All(FirmwareCatalog.All, f => Assert.Equal(64, f.BinSha256.Length));
    }

    [FirmwareFact]
    public void TheExtractedFirmware_IsRecognisedAsGenuine()
    {
        var report = FirmwareValidator.Validate(TestSupport.FirmwareFolder());

        Assert.True(report.IsGenuine);
        Assert.NotNull(report.Match);
    }

    [FirmwareFact]
    public void ChangingOneByte_IsReportedAsModified()
    {
        var copy = Path.Combine(work, "copy");
        Directory.CreateDirectory(copy);
        foreach (var name in FirmwareImporter.Wanted)
            File.Copy(Path.Combine(TestSupport.FirmwareFolder(), name), Path.Combine(copy, name));
        using (var engine = new FileStream(Path.Combine(copy, "m2engage"), FileMode.Open, FileAccess.Write))
        {
            engine.Position = 100;
            engine.WriteByte(0xAA);
        }

        var report = FirmwareValidator.Validate(copy);

        Assert.False(report.IsGenuine);
        Assert.NotNull(report.Match);
        Assert.Equal("m2engage", Assert.Single(report.Files, f => f.Verdict == FileVerdict.Modified).Name);
    }

    [NandFact]
    public void EveryArchive_UnpacksToFilesThatMatchTheCatalog()
    {
        var archives = Directory.GetFiles(Environment.GetEnvironmentVariable(NandFactAttribute.Variable)!, "*.7z");
        var seen = new HashSet<string>();

        foreach (var archive in archives)
        {
            var known = FirmwareValidator.IdentifyArchive(FirmwareValidator.Sha256(archive));
            Assert.NotNull(known);

            var folder = Path.Combine(work, Path.GetFileNameWithoutExtension(archive));
            var result = FirmwareImporter.Import(archive, folder);
            var report = FirmwareValidator.Validate(folder);

            Assert.Equal(FirmwareImporter.Wanted.Order(), result.Files.Order());
            Assert.Equal(ConsoleKind.MegaDriveMini, result.Console);
            Assert.True(report.IsGenuine, archive);
            Assert.Equal(known, report.Match);
            seen.Add(known!.Build);
        }

        Assert.Equal(archives.Length, seen.Count);
    }

    [Fact]
    public void ImportingAnArchiveWithoutAFirmwarePartition_Fails()
    {
        Directory.CreateDirectory(work);
        var path = Path.Combine(work, "broken.7z");
        File.WriteAllBytes(path, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.ThrowsAny<Exception>(() => FirmwareImporter.Import(path, Path.Combine(work, "out")));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

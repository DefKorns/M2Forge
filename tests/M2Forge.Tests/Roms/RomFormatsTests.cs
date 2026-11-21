using M2Forge.Core.Roms;

namespace M2Forge.Tests.Roms;

public sealed class RomFormatsTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "m2forge-fmt-" + Guid.NewGuid().ToString("N"));

    string Roms
    {
        get
        {
            var folder = Path.Combine(root, "system", "roms");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    void Write(string name, int size) => File.WriteAllBytes(Path.Combine(Roms, name), new byte[size]);

    [Fact]
    public void Scan_ListsEveryRomFormatAndSkipsOtherFiles()
    {
        Write("us_us_ecco.bin", 0x400);
        Write("jp_jp_puyo.smd", 0x300);
        Write("jp_jp_puyo2.smp", 0x300);
        Write("us_us_nighttrap.mcd", 0x500);
        Write("us_us_nighttrap.mcd.m", 0x10);
        Write("readme.txt", 0x10);

        var roms = RomLibrary.Scan(root);

        Assert.Equal(["bin", "smd", "smp", "mcd"], roms.OrderBy(r => RomLibrary.Formats.ToList().IndexOf(r.Format)).Select(r => r.Format));
        Assert.Equal(4, roms.Count);
    }

    [Fact]
    public void OnlyBinRomsGetAHeaderAndAreMegaDriveRoms()
    {
        var rom = new byte[0x400];
        System.Text.Encoding.ASCII.GetBytes("SEGA MEGA DRIVE ").CopyTo(rom, 0x100);
        File.WriteAllBytes(Path.Combine(Roms, "us_us_a.bin"), rom);
        File.WriteAllBytes(Path.Combine(Roms, "us_us_b.smd"), rom);

        var roms = RomLibrary.Scan(root).ToDictionary(r => r.Format);

        Assert.NotNull(roms["bin"].Header);
        Assert.True(roms["bin"].IsMegaDriveRom);
        Assert.Null(roms["smd"].Header);
        Assert.False(roms["smd"].IsMegaDriveRom);
    }

    [Fact]
    public void ExportName_KeepsTheNativeExtensionOfOtherFormats()
    {
        Write("us_us_a.bin", 0x400);
        Write("us_us_a.smd", 0x400);
        Write("us_us_a.mcd", 0x400);
        var roms = RomLibrary.Scan(root).ToDictionary(r => r.Format);

        Assert.Equal("us_us_a.md", RomExporter.ExportName(roms["bin"], ".md"));
        Assert.Equal("us_us_a.smd", RomExporter.ExportName(roms["smd"], ".md"));
        Assert.Equal("us_us_a.mcd", RomExporter.ExportName(roms["mcd"], ".gen"));
    }

    [Fact]
    public void Export_DoesNotCollideWhenTheSameTitleExistsInSeveralFormats()
    {
        Write("us_us_a.bin", 0x400);
        Write("us_us_a.smd", 0x400);
        var output = Path.Combine(root, "out");

        var result = RomExporter.Export(RomLibrary.Scan(root), output, ".md");

        Assert.Equal(2, result.Exported);
        Assert.Equal(["us_us_a.md", "us_us_a.smd"], Directory.GetFiles(output).Select(Path.GetFileName).Order());
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

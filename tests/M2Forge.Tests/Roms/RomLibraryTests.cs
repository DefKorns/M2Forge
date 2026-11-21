using System.Text;
using M2Forge.Core.Roms;

namespace M2Forge.Tests.Roms;

public sealed class RomLibraryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "m2forge-rom-" + Guid.NewGuid().ToString("N"));

    string RomsFolder => Path.Combine(root, "system", "roms");

    static byte[] BuildRom(string name = "ALEX KIDD", int size = 0x400, string console = "SEGA MEGA DRIVE ")
    {
        var rom = new byte[size];
        void Put(int offset, string text) => Encoding.ASCII.GetBytes(text).CopyTo(rom, offset);

        Put(0x100, console);
        Put(0x110, "(C)SEGA 1991.APR");
        Put(0x120, name.PadRight(48));
        Put(0x150, name.PadRight(48));
        Put(0x180, "GM 00001009-00");
        rom[0x18E] = 0x12;
        rom[0x18F] = 0x34;
        Put(0x1F0, "JUE");
        return rom;
    }

    string Write(string fileName, byte[] bytes)
    {
        Directory.CreateDirectory(RomsFolder);
        var path = Path.Combine(RomsFolder, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData("eu_en_Alex_Kidd", "eu", "en", "Alex Kidd")]
    [InlineData("us_jp_Dr_Robotnik_s_Mean_Bean_Machine", "us", "jp", "Dr Robotnik's Mean Bean Machine")]
    [InlineData("eu_en_Streets_of_Rage_2", "eu", "en", "Streets of Rage 2")]
    [InlineData("Tetris", "", "", "Tetris")]
    [InlineData("eu_Alex", "", "", "eu Alex")]
    [InlineData("EU_EN_Game", "", "", "EU EN Game")]
    public void ParseName_SplitsRegionLanguageAndTitle(string name, string region, string language, string title)
    {
        Assert.Equal((region, language, title), RomLibrary.ParseName(name));
    }

    [Fact]
    public void ReadHeader_ParsesTheStandardFields()
    {
        var header = RomLibrary.ReadHeader(BuildRom("ALEX KIDD IN THE ENCHANTED CASTLE"));

        Assert.NotNull(header);
        Assert.Equal("SEGA MEGA DRIVE", header.Console);
        Assert.Equal("(C)SEGA 1991.APR", header.Copyright);
        Assert.Equal("ALEX KIDD IN THE ENCHANTED CASTLE", header.DomesticName);
        Assert.Equal("ALEX KIDD IN THE ENCHANTED CASTLE", header.OverseasName);
        Assert.Equal("GM 00001009-00", header.Serial);
        Assert.Equal(0x1234, header.Checksum);
        Assert.Equal("JUE", header.Regions);
    }

    [Fact]
    public void ReadHeader_AcceptsGenesisAndRejectsOtherData()
    {
        Assert.NotNull(RomLibrary.ReadHeader(BuildRom(console: "SEGA GENESIS    ")));
        Assert.Null(RomLibrary.ReadHeader(BuildRom(console: "NOT A MEGA ROM  ")));
        Assert.Null(RomLibrary.ReadHeader(new byte[0x100]));
    }

    [Fact]
    public void ReadHeader_ReplacesNonPrintableBytes()
    {
        var rom = BuildRom();
        rom[0x120] = 0x83;
        rom[0x121] = 0x00;
        rom[0x122] = (byte)'X';

        var header = RomLibrary.ReadHeader(rom)!;

        Assert.StartsWith("X", header.DomesticName);
    }

    [Fact]
    public void Scan_ListsBinFilesSortedByTitle()
    {
        Write("eu_jp_Zeta_Game.bin", BuildRom("ZETA"));
        Write("eu_en_Alpha_Game.bin", BuildRom("ALPHA", 0x800));
        Write("us_en_Alpha_Game.bin", BuildRom("ALPHA"));
        Write("notes.txt", [1, 2, 3]);

        var roms = RomLibrary.Scan(root);

        Assert.Equal(["Alpha Game", "Alpha Game", "Zeta Game"], roms.Select(r => r.Title));
        Assert.Equal(["eu", "us", "eu"], roms.Select(r => r.Region));
        Assert.Equal(0x800, roms[0].Size);
        Assert.Equal("ALPHA", roms[0].HeaderName);
        Assert.Equal("eu_en_Alpha_Game.bin", roms[0].FileName);
    }

    [Fact]
    public void Scan_KeepsFilesWithoutAHeaderAndHandlesMissingFolders()
    {
        Assert.Empty(RomLibrary.Scan(root));

        Write("eu_en_Tiny.bin", [1, 2, 3]);
        var rom = Assert.Single(RomLibrary.Scan(root));

        Assert.Null(rom.Header);
        Assert.Equal(string.Empty, rom.HeaderName);
        Assert.Equal(3, rom.Size);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

using System.Text;
using M2Forge.Core.Discs;
using M2Forge.Core.Roms;

namespace M2Forge.Tests.Discs;

public class SegaCdBiosTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-bios-" + Guid.NewGuid().ToString("N"));

    RomInfo Bios(string fileName, string title, char region)
    {
        Directory.CreateDirectory(work);
        var data = new byte[0x20000];
        Encoding.ASCII.GetBytes(title).CopyTo(data, 0x120);
        data[0x1F0] = (byte)region;
        var path = Path.Combine(work, fileName);
        File.WriteAllBytes(path, data);
        return new RomInfo(fileName, path, "xx", "xx", fileName, data.Length, null);
    }

    [Fact]
    public void BiosFiles_GetTheNamesRetroArchExpects()
    {
        var us = Bios("us_us_bios01.bin", "SEGA-CD BOOT ROM", 'U');
        var eu = Bios("eu_eu_bios.bin", "CD2 BOOT ROM", 'E');
        var jp = Bios("jp_jp_bios01.bin", "MEGA-CD BOOT ROM", 'J');

        var selected = SegaCdBios.Select([jp, eu, us]);

        Assert.Equal(["bios_CD_E.bin", "bios_CD_J.bin", "bios_CD_U.bin"], selected.Select(b => b.RetroArchName));
        Assert.Equal(eu.Path, selected[0].Rom.Path);
    }

    [Fact]
    public void WhenARegionHasSeveralBios_TheFirstByNameWins_AndWonderMegaIsIgnored()
    {
        var first = Bios("jp_jp_bios01.bin", "MEGA-CD BOOT ROM", 'J');
        var second = Bios("jp_jp_bios02.bin", "CD2 BOOT ROM", 'J');
        var wonder = Bios("jp_jp_bios_wonder.bin", "WONDER-MEGA BOOTROM", 'J');

        var selected = SegaCdBios.Select([wonder, second, first]);

        Assert.Equal(first.Path, Assert.Single(selected).Rom.Path);
    }

    [Fact]
    public void AGameThatIsNotABios_IsNotListed()
    {
        var game = Bios("us_us_game.bin", "SOMETHING ELSE", 'U');

        Assert.Empty(SegaCdBios.Select([game]));
    }

    [Fact]
    public void Export_WritesTheRenamedCopyAndKeepsExistingFilesUnlessAsked()
    {
        var us = Bios("us_us_bios01.bin", "SEGA-CD BOOT ROM", 'U');
        var files = SegaCdBios.Select([us]);
        var target = Path.Combine(work, "system");

        var first = SegaCdBios.Export(files, target, overwrite: false);
        var second = SegaCdBios.Export(files, target, overwrite: false);

        Assert.Equal(1, first.Exported);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(File.ReadAllBytes(us.Path), File.ReadAllBytes(Path.Combine(target, "bios_CD_U.bin")));
    }

    [Theory]
    [InlineData("us", "bios_CD_U.bin")]
    [InlineData("EU", "bios_CD_E.bin")]
    [InlineData("jp", "bios_CD_J.bin")]
    public void TheNeededBiosFollowsTheDiscRegion(string region, string name)
    {
        Assert.Equal(name, SegaCdBios.NameForRegion(region));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

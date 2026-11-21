using M2Forge.Core.Discs;

namespace M2Forge.Tests.Discs;

public sealed class DiscConverterTests
{
    [McdFact]
    public async Task ARealDisc_BecomesABinCueWhoseDataTrackMatchesTheIso()
    {
        var mcd = Environment.GetEnvironmentVariable(McdFactAttribute.Variable)!;

        var folder = Path.Combine(Path.GetTempPath(), "m2forge-disc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var images = await DiscConverter.ConvertAsync(mcd,Path.Combine(folder, "us_us_test_cd"));

            var image = Assert.Single(images);
            Assert.Equal("us_us_test_cd.cue", Path.GetFileName(image.CuePath));
            var cue = CueSheet.Parse(await File.ReadAllLinesAsync(image.CuePath));
            Assert.False(cue[0].IsAudio);
            Assert.Equal(0, cue[0].Index01);
            Assert.Equal(0, new FileInfo(image.BinPath).Length % CdSector.RawSize);

            using var bin = File.OpenRead(image.BinPath);
            var sector = new byte[CdSector.RawSize];
            bin.Position = 16L * CdSector.RawSize;
            bin.ReadExactly(sector);
            Assert.Equal("CD001", System.Text.Encoding.ASCII.GetString(sector, 17, 5));

            bin.Position = 0;
            bin.ReadExactly(sector);
            Assert.Equal("SEGADISCSYSTEM", System.Text.Encoding.ASCII.GetString(sector, 16, 14));
            Assert.True(CdSector.IsValidMode1(sector));
            Assert.DoesNotContain(Directory.GetFileSystemEntries(folder), p => Path.GetFileName(p).StartsWith('.'));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}

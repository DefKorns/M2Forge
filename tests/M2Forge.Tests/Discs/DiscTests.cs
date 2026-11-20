using M2Forge.Core.Discs;

namespace M2Forge.Tests.Discs;

public class CdSectorTests
{
    [Fact]
    public void AMode1Sector_HasSyncHeaderAndTheDataInPlace()
    {
        var data = Enumerable.Range(0, 2048).Select(i => (byte)i).ToArray();
        var sector = new byte[CdSector.RawSize];

        CdSector.WriteMode1(data, 16, sector);

        Assert.Equal(new byte[] { 0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0 }, sector[..12]);
        Assert.Equal(new byte[] { 0x00, 0x02, 0x16, 0x01 }, sector[12..16]);
        Assert.Equal(data, sector[16..2064]);
        Assert.True(CdSector.IsValidMode1(sector));
    }

    [Fact]
    public void ACorruptedSector_IsRejected()
    {
        var sector = new byte[CdSector.RawSize];
        CdSector.WriteMode1(new byte[2048], 0, sector);
        sector[100] ^= 1;

        Assert.False(CdSector.IsValidMode1(sector));
    }

    [Fact]
    public void TheEdcOfNothingIsZero()
    {
        Assert.Equal(0u, CdSector.ComputeEdc([]));
    }

    [Fact]
    public void TheEightByteCheckVector_MatchesTheCrc()
    {
        Assert.Equal(0u, CdSector.ComputeEdc(new byte[16]));
    }
}

public class CueSheetTests
{
    [Fact]
    public void ParsesTracksPregapsAndIndexes()
    {
        string[] lines =
        [
            "FILE \"01 x.iso\" BINARY",
            "  TRACK 01 MODE1/2048",
            "    PREGAP 00:02:00",
            "    INDEX 01 00:00:00",
            "FILE \"02 x.wav\" WAVE",
            "  TRACK 02 AUDIO",
            "    INDEX 00 00:00:00",
            "    INDEX 01 00:02:00",
        ];

        var tracks = CueSheet.Parse(lines);

        Assert.Equal(2, tracks.Count);
        Assert.False(tracks[0].IsAudio);
        Assert.Equal(150, tracks[0].Pregap);
        Assert.True(tracks[1].IsAudio);
        Assert.Equal(0, tracks[1].Index00);
        Assert.Equal(150, tracks[1].Index01);
        Assert.Equal("02 x.wav", tracks[1].File);
    }

    [Fact]
    public void MsfRoundTrips()
    {
        Assert.Equal("01:02:03", CueSheet.Msf(CueSheet.ToFrames("01:02:03")));
    }
}

public class ChdTests
{
    [McdFact]
    public async Task ACompressedDisc_ExtractsBackToTheSameBin()
    {
        var chdman = Environment.GetEnvironmentVariable("M2FORGE_TEST_CHDMAN");
        if (string.IsNullOrEmpty(chdman) || !File.Exists(chdman)) return;

        var mcd = Environment.GetEnvironmentVariable(McdFactAttribute.Variable)!;
        var folder = Path.Combine(Path.GetTempPath(), "m2forge-chd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plain = await DiscConverter.ConvertAsync(mcd, Path.Combine(folder, "plain", "disc"));
            var chd = Path.Combine(folder, "disc.chd");
            await Chdman.CompressAsync(chdman, plain[0].CuePath, chd);
            Assert.True(new FileInfo(chd).Length < new FileInfo(plain[0].BinPath).Length);

            var back = Path.Combine(folder, "back.cue");
            var start = new System.Diagnostics.ProcessStartInfo(chdman) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "extractcd", "-i", chd, "-o", back, "-ob", Path.Combine(folder, "back.bin") }) start.ArgumentList.Add(a);
            using (var process = System.Diagnostics.Process.Start(start)!) await process.WaitForExitAsync();

            Assert.Equal(await File.ReadAllBytesAsync(plain[0].BinPath), await File.ReadAllBytesAsync(Path.Combine(folder, "back.bin")));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}

public class DiscConverterTests
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

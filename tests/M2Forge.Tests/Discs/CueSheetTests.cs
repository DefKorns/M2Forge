using M2Forge.Core.Discs;

namespace M2Forge.Tests.Discs;

public sealed class CueSheetTests
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

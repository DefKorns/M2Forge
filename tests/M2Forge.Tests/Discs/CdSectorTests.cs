using M2Forge.Core.Discs;

namespace M2Forge.Tests.Discs;

public sealed class CdSectorTests
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

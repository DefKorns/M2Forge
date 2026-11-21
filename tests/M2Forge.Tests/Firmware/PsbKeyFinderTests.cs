using System.Text;
using M2Forge.Core.Firmware;

namespace M2Forge.Tests.Firmware;

public sealed class PsbKeyFinderTests
{
    static byte[] Build(string key) =>
        [.. "junk"u8.ToArray(), 0, .. "getExistFileDirInMountArchive"u8.ToArray(), 0, 0, 0, .. Encoding.ASCII.GetBytes(key), 0, 0, 0, 77];

    [Fact]
    public void Find_ReturnsKeyAfterMarker()
    {
        Assert.Equal("nY/RHn+XH8T77", PsbKeyFinder.Find(Build("nY/RHn+XH8T77")));
    }

    [Fact]
    public void Find_ReturnsNullWithoutMarker()
    {
        Assert.Null(PsbKeyFinder.Find("nothing here"u8));
    }

    [Fact]
    public void Find_ReturnsNullWhenMarkerIsLast()
    {
        Assert.Null(PsbKeyFinder.Find("getExistFileDirInMountArchive\0\0\0"u8));
    }

    [FirmwareFact]
    public void Find_MatchesRealFirmware()
    {
        var folder = Environment.GetEnvironmentVariable(FirmwareFactAttribute.Variable)!;
        Assert.Equal("nY/RHn+XH8T77", PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage")));
    }
}

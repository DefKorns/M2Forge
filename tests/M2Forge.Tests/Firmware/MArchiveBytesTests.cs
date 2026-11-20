using GMWare.M2.MArchive;
using M2Forge.Core.Firmware;

namespace M2Forge.Tests.Firmware;

public class MArchiveBytesTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-mab-" + Guid.NewGuid().ToString("N"));
    readonly MArchiveBytes bytes = new(ConsoleProfile.MegaDriveMini, "seed-for-tests");

    static byte[] Sample()
    {
        var data = new byte[100_000];
        new Random(9).NextBytes(data.AsSpan(0, 20_000));
        return data;
    }

    [Fact]
    public void CompressThenDecompress_ReturnsTheOriginal()
    {
        var data = Sample();
        var stored = bytes.Compress("sample.nut.m", data);

        Assert.True(stored.Length < data.Length);
        Assert.Equal(data, bytes.Decompress("sample.nut.m", stored));
    }

    [Fact]
    public void Decompress_UsesTheFileNameInTheKey()
    {
        var stored = bytes.Compress("a.nut.m", Sample());
        Assert.ThrowsAny<Exception>(() => bytes.Decompress("b.nut.m", stored));
    }

    [Fact]
    public void Decompress_RejectsWrongMagic()
    {
        Assert.Throws<InvalidDataException>(() => bytes.Decompress("x.m", new byte[64]));
        Assert.Throws<InvalidDataException>(() => bytes.Decompress("x.m", [1, 2]));
    }

    [Fact]
    public void OutputIsReadableByTheLibraryPacker()
    {
        Directory.CreateDirectory(work);
        var data = Sample();
        var path = Path.Combine(work, "sample.nut.m");
        File.WriteAllBytes(path, bytes.Compress("sample.nut.m", data));

        new MArchivePacker(new ZstdSharpCodec(), "seed-for-tests", 64).DecompressFile(path);

        Assert.Equal(data, File.ReadAllBytes(Path.Combine(work, "sample.nut")));
    }

    [Fact]
    public void ReadsWhatTheLibraryPackerWrites()
    {
        Directory.CreateDirectory(work);
        var data = Sample();
        var path = Path.Combine(work, "sample.nut");
        File.WriteAllBytes(path, data);

        new MArchivePacker(new ZstdSharpCodec(), "seed-for-tests", 64).CompressFile(path);

        Assert.Equal(data, bytes.Decompress("sample.nut.m", File.ReadAllBytes(path + ".m")));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

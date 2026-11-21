using M2Forge.Core.Textures;

namespace M2Forge.Tests.Textures;

public sealed class TextureLibraryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "m2forge-lib-" + Guid.NewGuid().ToString("N"));

    void WriteMotion(string relative, string sourceJson, byte[] stream)
    {
        var json = Path.Combine(root, relative + ".psb.json");
        Directory.CreateDirectory(Path.GetDirectoryName(json)!);
        File.WriteAllText(json, "{\"id\":\"motion\",\"source\":" + sourceJson + "}");
        var streams = Path.Combine(root, relative + ".psb.streams");
        Directory.CreateDirectory(streams);
        File.WriteAllBytes(Path.Combine(streams, "stream_0"), stream);
    }

    [Fact]
    public void Scan_FindsMotionObjectsAndFontArrays()
    {
        WriteMotion(
            "system/motion/a",
            "{\"tex\":{\"texture\":{\"type\":\"RGBA8\",\"width\":2,\"height\":1,\"pixel\":\"_stream:0\"},\"icon\":{\"i\":{\"left\":1,\"top\":0,\"width\":1,\"height\":1}}}}",
            [1, 2, 3, 4, 5, 6, 7, 8]);
        WriteMotion(
            "system/font/b",
            "[{\"type\":\"A8\",\"width\":2,\"height\":1,\"pixel\":\"_stream:0\"}]",
            [9, 10]);

        var scan = TextureLibrary.Scan(root);

        Assert.Empty(scan.Failures);
        Assert.Equal(2, scan.TextureCount);
        Assert.Equal(["system/font/b", "system/motion/a"], scan.Files.Select(f => f.Name));

        var motion = scan.Files[1].Sources[0];
        Assert.Equal("tex", motion.Name);
        Assert.Equal(new IconInfo("i", 1, 0, 1, 1), Assert.Single(motion.Icons));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, motion.Load().Crop(1, 0, 1, 1).Pixels);

        Assert.Equal("0", scan.Files[0].Sources[0].Name);
    }

    [Fact]
    public void Scan_ReportsBrokenFilesAndContinues()
    {
        var broken = Path.Combine(root, "broken.psb.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(broken, "{ not json");
        WriteMotion("ok", "[{\"type\":\"A8\",\"width\":1,\"height\":1,\"pixel\":\"_stream:0\"}]", [1]);

        var scan = TextureLibrary.Scan(root);

        Assert.Single(scan.Failures);
        Assert.Equal(1, scan.TextureCount);
    }

    [FirmwareFact]
    public void Scan_FindsEveryTextureInFirmware()
    {
        var reference = Environment.GetEnvironmentVariable("M2FORGE_TEST_REFERENCE");
        Assert.True(!string.IsNullOrEmpty(reference) && Directory.Exists(reference), "Set M2FORGE_TEST_REFERENCE.");

        var scan = TextureLibrary.Scan(reference!);

        Assert.Empty(scan.Failures);
        Assert.Equal(126, scan.TextureCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

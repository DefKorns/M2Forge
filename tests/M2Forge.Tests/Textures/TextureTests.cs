using M2Forge.Core.Textures;

namespace M2Forge.Tests.Textures;

public class TextureTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-tex-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Decode_Rgba8_KeepsBytes()
    {
        var image = RgbaImage.Decode(TextureFormat.Rgba8, [1, 2, 3, 4, 5, 6, 7, 8], 2, 1);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, image.Pixels);
    }

    [Fact]
    public void Decode_A8L8_ReadsLuminanceThenAlpha()
    {
        var image = RgbaImage.Decode(TextureFormat.A8L8, [10, 200, 30, 40], 2, 1);
        Assert.Equal(new byte[] { 10, 10, 10, 200, 30, 30, 30, 40 }, image.Pixels);
    }

    [Fact]
    public void Decode_A8_BecomesWhiteWithAlpha()
    {
        var image = RgbaImage.Decode(TextureFormat.A8, [0, 128], 2, 1);
        Assert.Equal(new byte[] { 255, 255, 255, 0, 255, 255, 255, 128 }, image.Pixels);
    }

    [Fact]
    public void Decode_ShortData_Throws()
    {
        Assert.Throws<InvalidDataException>(() => RgbaImage.Decode(TextureFormat.Rgba8, new byte[7], 2, 1));
    }

    [Fact]
    public void Crop_InsideAndOutsideBounds()
    {
        var pixels = new byte[3 * 2 * 4];
        for (var i = 0; i < 6; i++) pixels[i * 4] = pixels[i * 4 + 3] = (byte)(i + 1);
        var image = new RgbaImage(3, 2, pixels);

        var inside = image.Crop(1, 0, 2, 2);
        Assert.Equal(new byte[] { 2, 0, 0, 2, 3, 0, 0, 3, 5, 0, 0, 5, 6, 0, 0, 6 }, inside.Pixels);

        var outside = image.Crop(-1, 1, 3, 2);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 4, 0, 0, 4, 5, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, outside.Pixels);
    }

    [Fact]
    public void PngEncoder_ProducesValidPng()
    {
        var pixels = new byte[5 * 3 * 4];
        new Random(7).NextBytes(pixels);
        var image = new RgbaImage(5, 3, pixels);

        using var stream = new MemoryStream();
        PngEncoder.Write(image, stream);

        var (width, height, rgba) = PngReader.Read(stream.ToArray());
        Assert.Equal(5, width);
        Assert.Equal(3, height);
        Assert.Equal(pixels, rgba);
    }

    [FirmwareFact]
    public void ExportTree_ConvertsEveryTexture()
    {
        var reference = Environment.GetEnvironmentVariable("M2FORGE_TEST_REFERENCE");
        Assert.True(!string.IsNullOrEmpty(reference) && Directory.Exists(reference), "Set M2FORGE_TEST_REFERENCE.");

        var result = TextureExporter.ExportTree(reference!, work);

        Assert.Empty(result.Failures);
        Assert.Equal(126, result.Textures);
        Assert.True(result.Icons > 0);

        var (width, height, _) = PngReader.Read(File.ReadAllBytes(Path.Combine(work, "system", "motion", "titleselect_ui", "tex#000.png")));
        Assert.Equal(1024, width);
        Assert.Equal(1024, height);
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

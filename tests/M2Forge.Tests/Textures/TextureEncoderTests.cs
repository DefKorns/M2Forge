using M2Forge.Core.Textures;

namespace M2Forge.Tests.Textures;

public sealed class TextureEncoderTests
{
    [Fact]
    public void Encode_Rgba8_CopiesPixels()
    {
        var image = new RgbaImage(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, TextureEncoder.Encode(image, TextureFormat.Rgba8));
    }

    [Fact]
    public void Encode_A8L8_UsesLumaAndAlpha()
    {
        var image = new RgbaImage(4, 1, [
            255, 0, 0, 10,
            0, 255, 0, 20,
            0, 0, 255, 30,
            255, 255, 255, 255]);

        Assert.Equal(
            new byte[] { 54, 10, 182, 20, 18, 30, 255, 255 },
            TextureEncoder.Encode(image, TextureFormat.A8L8));
    }

    [Fact]
    public void Encode_A8_KeepsOnlyAlpha()
    {
        var image = new RgbaImage(2, 1, [9, 9, 9, 0, 200, 100, 50, 128]);
        Assert.Equal(new byte[] { 0, 128 }, TextureEncoder.Encode(image, TextureFormat.A8));
    }

    [Fact]
    public void Encode_L8_KeepsOnlyLuma()
    {
        var image = new RgbaImage(2, 1, [255, 0, 0, 255, 77, 77, 77, 0]);
        Assert.Equal(new byte[] { 54, 77 }, TextureEncoder.Encode(image, TextureFormat.L8));
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8)]
    [InlineData(TextureFormat.A8L8)]
    [InlineData(TextureFormat.A8)]
    [InlineData(TextureFormat.L8)]
    public void Encode_IsTheExactInverseOfDecode(TextureFormat format)
    {
        var stream = new byte[16 * 8 * format.BytesPerPixel()];
        new Random(5).NextBytes(stream);

        var decoded = RgbaImage.Decode(format, stream, 16, 8);

        Assert.Equal(stream, TextureEncoder.Encode(decoded, format));
    }

    [Fact]
    public void WriteRegion_TouchesOnlyTheRegion()
    {
        var stream = Enumerable.Repeat((byte)0xEE, 4 * 3 * 2).ToArray();
        var region = new RgbaImage(2, 1, [10, 10, 10, 1, 20, 20, 20, 2]);

        TextureEncoder.WriteRegion(stream, TextureFormat.A8L8, 4, 3, 1, 2, region);

        var changed = Enumerable.Range(0, stream.Length).Where(i => stream[i] != 0xEE).ToList();
        Assert.Equal([18, 19, 20, 21], changed);
        Assert.Equal(new byte[] { 10, 1, 20, 2 }, stream[18..22]);
    }

    [Fact]
    public void WriteRegion_RejectsRegionsOutsideTheTexture()
    {
        var stream = new byte[4 * 3 * 4];
        var region = new RgbaImage(2, 2, new byte[16]);

        Assert.Throws<ArgumentOutOfRangeException>(() => TextureEncoder.WriteRegion(stream, TextureFormat.Rgba8, 4, 3, 3, 0, region));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextureEncoder.WriteRegion(stream, TextureFormat.Rgba8, 4, 3, 0, 2, region));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextureEncoder.WriteRegion(stream, TextureFormat.Rgba8, 4, 3, -1, 0, region));
    }

    [Fact]
    public void WriteRegion_RejectsShortStream()
    {
        var region = new RgbaImage(1, 1, new byte[4]);
        Assert.Throws<ArgumentException>(() => TextureEncoder.WriteRegion(new byte[10], TextureFormat.Rgba8, 4, 3, 0, 0, region));
    }

    [Fact]
    public void RoundTrip_ShowsWhatTheFormatKeeps()
    {
        var image = new RgbaImage(2, 1, [255, 0, 0, 200, 10, 20, 30, 255]);

        Assert.Equal(image.Pixels, TextureEncoder.RoundTrip(image, TextureFormat.Rgba8).Pixels);
        Assert.Equal(new byte[] { 54, 54, 54, 200, 19, 19, 19, 255 }, TextureEncoder.RoundTrip(image, TextureFormat.A8L8).Pixels);
        Assert.Equal(new byte[] { 255, 255, 255, 200, 255, 255, 255, 255 }, TextureEncoder.RoundTrip(image, TextureFormat.A8).Pixels);
    }

    [Fact]
    public void Paste_ReplacesPixelsAndClipsWithoutTouchingTheSource()
    {
        var atlas = new RgbaImage(3, 2, Enumerable.Repeat((byte)7, 24).ToArray());
        var patch = new RgbaImage(2, 2, Enumerable.Repeat((byte)9, 16).ToArray());

        var result = atlas.Paste(patch, 2, 1);

        Assert.Equal(Enumerable.Repeat((byte)7, 24), atlas.Pixels);
        var changed = Enumerable.Range(0, 24).Where(i => result.Pixels[i] != 7).ToList();
        Assert.Equal(Enumerable.Range(20, 4), changed);
    }

    [FirmwareFact]
    public void EveryFirmwareTexture_EncodeInvertsDecodeByteForByte()
    {
        var reference = Environment.GetEnvironmentVariable("M2FORGE_TEST_REFERENCE");
        Assert.True(!string.IsNullOrEmpty(reference) && Directory.Exists(reference), "Set M2FORGE_TEST_REFERENCE.");

        var scan = TextureLibrary.Scan(reference!);
        var mismatches = new List<string>();
        foreach (var file in scan.Files)
        {
            foreach (var source in file.Sources)
            {
                var stream = File.ReadAllBytes(source.StreamPath);
                var expected = source.Width * source.Height * source.Format.BytesPerPixel();
                var encoded = TextureEncoder.Encode(source.Load(), source.Format);

                if (stream.Length != expected) mismatches.Add($"{file.Name}/{source.Name}: stream {stream.Length}, expected {expected}");
                else if (!encoded.AsSpan().SequenceEqual(stream)) mismatches.Add($"{file.Name}/{source.Name}: bytes differ");
            }
        }

        Assert.Empty(mismatches);
        Assert.Equal(126, scan.TextureCount);
    }
}

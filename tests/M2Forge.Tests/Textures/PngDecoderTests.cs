using M2Forge.Core.Textures;

namespace M2Forge.Tests.Textures;

public class PngDecoderTests
{
    static byte[][] Rows(byte[] flat, int rowLength) =>
        Enumerable.Range(0, flat.Length / rowLength).Select(i => flat[(i * rowLength)..((i + 1) * rowLength)]).ToArray();

    [Fact]
    public void Rgba8_DecodesWithEveryFilterType()
    {
        var pixels = new byte[5 * 5 * 4];
        new Random(11).NextBytes(pixels);

        var png = PngBuilder.Build(5, 5, 8, 6, Rows(pixels, 20), filters: [0, 1, 2, 3, 4]);

        Assert.Equal(pixels, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void Rgb8_GetsOpaqueAlpha()
    {
        var png = PngBuilder.Build(2, 1, 8, 2, [[10, 20, 30, 40, 50, 60]]);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 }, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void Gray8_ExpandsToRgb()
    {
        var png = PngBuilder.Build(3, 1, 8, 0, [[0, 128, 255]]);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 128, 128, 128, 255, 255, 255, 255, 255 }, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void GrayAlpha8_KeepsAlpha()
    {
        var png = PngBuilder.Build(2, 1, 8, 4, [[100, 200, 50, 25]]);
        Assert.Equal(new byte[] { 100, 100, 100, 200, 50, 50, 50, 25 }, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void Rgba16_KeepsTheHighByte()
    {
        var png = PngBuilder.Build(1, 1, 16, 6, [[0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0]]);
        Assert.Equal(new byte[] { 0x12, 0x56, 0x9A, 0xDE }, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void Gray2Bit_ScalesToFullRange()
    {
        var png = PngBuilder.Build(4, 1, 2, 0, [[0b00_01_10_11]]);
        var pixels = PngDecoder.Decode(png).Pixels;

        Assert.Equal(new byte[] { 0, 85, 170, 255 }, new[] { pixels[0], pixels[4], pixels[8], pixels[12] });
    }

    [Fact]
    public void Palette4Bit_UsesTransparencyTable()
    {
        byte[] palette = [255, 0, 0, 0, 255, 0, 0, 0, 255];
        var png = PngBuilder.Build(3, 1, 4, 3, [[0x20, 0x10]], palette: palette, transparency: [255, 128]);

        Assert.Equal(
            new byte[] { 0, 0, 255, 255, 255, 0, 0, 255, 0, 255, 0, 128 },
            PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void Palette1Bit_ReadsBitsLeftToRight()
    {
        byte[] palette = [0, 0, 0, 255, 255, 255];
        var png = PngBuilder.Build(9, 1, 1, 3, [[0xAA, 0x80]], palette: palette);
        var pixels = PngDecoder.Decode(png).Pixels;

        var red = Enumerable.Range(0, 9).Select(x => pixels[x * 4]).ToArray();
        Assert.Equal(new byte[] { 255, 0, 255, 0, 255, 0, 255, 0, 255 }, red);
    }

    [Fact]
    public void Rgb8_ColourKeyBecomesTransparent()
    {
        var png = PngBuilder.Build(2, 1, 8, 2, [[10, 20, 30, 1, 2, 3]], transparency: [0, 10, 0, 20, 0, 30]);
        Assert.Equal(new byte[] { 10, 20, 30, 0, 1, 2, 3, 255 }, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void Interlaced_IsRejectedWithAClearMessage()
    {
        var png = PngBuilder.Build(1, 1, 8, 6, [[1, 2, 3, 4]], interlace: 1);
        var error = Assert.Throws<NotSupportedException>(() => PngDecoder.Decode(png));
        Assert.Contains("nterlac", error.Message);
    }

    [Fact]
    public void PaletteIndexOutOfRange_Throws()
    {
        var png = PngBuilder.Build(1, 1, 8, 3, [[5]], palette: [0, 0, 0]);
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Corruption_IsDetected()
    {
        var png = PngBuilder.Build(2, 1, 8, 6, [[1, 2, 3, 4, 5, 6, 7, 8]]);

        var damaged = (byte[])png.Clone();
        damaged[16] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(damaged));

        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(png[..^15]));
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(new byte[40]));
    }

    [Fact]
    public void RoundTripsWithOwnEncoder()
    {
        var pixels = new byte[7 * 4 * 4];
        new Random(3).NextBytes(pixels);
        var image = new RgbaImage(7, 4, pixels);

        using var stream = new MemoryStream();
        PngEncoder.Write(image, stream);
        var decoded = PngDecoder.Decode(stream.ToArray());

        Assert.Equal(7, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Assert.Equal(pixels, decoded.Pixels);
    }
}

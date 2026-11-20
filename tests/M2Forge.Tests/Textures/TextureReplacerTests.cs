using M2Forge.Core.Textures;

namespace M2Forge.Tests.Textures;

public class TextureReplacerTests
{
    static TextureSource Source(TextureFormat format, int width, int height, params IconInfo[] icons) =>
        new("tex", format, width, height, "unused", icons);

    [Fact]
    public void Replace_Icon_ChangesOnlyThatRectangle()
    {
        var icon = new IconInfo("0000", 1, 1, 2, 2);
        var source = Source(TextureFormat.Rgba8, 4, 4, icon);
        var stream = new byte[4 * 4 * 4];
        new Random(2).NextBytes(stream);
        var untouched = (byte[])stream.Clone();

        var image = new RgbaImage(2, 2, Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());
        var result = TextureReplacer.Replace(source, stream, image, icon);

        Assert.Equal(untouched, stream);
        var changed = Enumerable.Range(0, stream.Length).Where(i => result[i] != stream[i]).ToList();
        Assert.All(changed, i =>
        {
            var pixel = i / 4;
            Assert.InRange(pixel % 4, 1, 2);
            Assert.InRange(pixel / 4, 1, 2);
        });
        var firstRow = (1 * 4 + 1) * 4;
        var secondRow = (2 * 4 + 1) * 4;
        Assert.Equal(image.Pixels[..8], result[firstRow..(firstRow + 8)]);
        Assert.Equal(image.Pixels[8..], result[secondRow..(secondRow + 8)]);
    }

    [Fact]
    public void Replace_WholeTexture_NeedsTheSameSize()
    {
        var source = Source(TextureFormat.A8, 3, 2);
        var stream = new byte[6];

        var result = TextureReplacer.Replace(source, stream, new RgbaImage(3, 2, [
            0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3,
            0, 0, 0, 4, 0, 0, 0, 5, 0, 0, 0, 6]));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, result);
        Assert.Throws<ArgumentException>(() => TextureReplacer.Replace(source, stream, new RgbaImage(2, 2, new byte[16])));
    }

    [Fact]
    public void Replace_Icon_RejectsWrongSizeWithTheIconName()
    {
        var icon = new IconInfo("logo", 0, 0, 2, 2);
        var source = Source(TextureFormat.Rgba8, 4, 4, icon);

        var error = Assert.Throws<ArgumentException>(() =>
            TextureReplacer.Replace(source, new byte[64], new RgbaImage(3, 2, new byte[24]), icon));

        Assert.Contains("logo", error.Message);
    }

    [Fact]
    public void Replace_KeepsPaddingAfterTheTexture()
    {
        var source = Source(TextureFormat.A8, 2, 1);
        var stream = new byte[] { 1, 2, 77, 88 };

        var result = TextureReplacer.Replace(source, stream, new RgbaImage(2, 1, [0, 0, 0, 9, 0, 0, 0, 8]));

        Assert.Equal(new byte[] { 9, 8, 77, 88 }, result);
    }

    [Fact]
    public void Replace_RejectsShortStream()
    {
        var source = Source(TextureFormat.Rgba8, 2, 2);
        Assert.Throws<InvalidDataException>(() =>
            TextureReplacer.Replace(source, new byte[8], new RgbaImage(2, 2, new byte[16])));
    }
}

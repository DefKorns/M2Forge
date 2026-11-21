using M2Forge.Core.Textures;

namespace M2Forge.Tests.ViewModels;

public sealed class RgbaImageThumbnailTests
{
    static RgbaImage Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            pixels[i * 4] = r;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = b;
            pixels[i * 4 + 3] = a;
        }
        return new RgbaImage(width, height, pixels);
    }

    [Fact]
    public void ASmallImage_IsReturnedAsIs()
    {
        var image = Solid(20, 10, 1, 2, 3);

        Assert.Same(image, image.Thumbnail(64));
    }

    [Fact]
    public void ALargeImage_KeepsItsAspectRatioAndColour()
    {
        var thumb = Solid(400, 200, 200, 100, 50).Thumbnail(100);

        Assert.Equal((100, 50), (thumb.Width, thumb.Height));
        Assert.Equal([200, 100, 50, 255], thumb.Pixels[..4]);
        Assert.Equal([200, 100, 50, 255], thumb.Pixels[^4..]);
    }

    [Fact]
    public void ATallImage_IsLimitedByItsHeight()
    {
        var thumb = Solid(100, 400, 9, 9, 9).Thumbnail(80);

        Assert.Equal((20, 80), (thumb.Width, thumb.Height));
    }

    [Fact]
    public void TransparentPixels_DoNotBleedTheirColourIntoNeighbours()
    {
        var pixels = new byte[4 * 1 * 4];
        new byte[] { 255, 0, 0, 255, 0, 255, 0, 0 }.CopyTo(pixels, 0);
        new byte[] { 255, 0, 0, 255, 0, 255, 0, 0 }.CopyTo(pixels, 8);
        var thumb = new RgbaImage(4, 1, pixels).Thumbnail(2);

        Assert.Equal((2, 1), (thumb.Width, thumb.Height));
        Assert.Equal(255, thumb.Pixels[0]);
        Assert.Equal(0, thumb.Pixels[1]);
        Assert.InRange(thumb.Pixels[3], 120, 135);
    }

    [Fact]
    public void ANonPositiveSize_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Solid(4, 4, 0, 0, 0).Thumbnail(0));
    }
}

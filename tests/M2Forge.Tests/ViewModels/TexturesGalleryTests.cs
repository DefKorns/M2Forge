using M2Forge.App.ViewModels;
using M2Forge.Core.Textures;

namespace M2Forge.Tests.ViewModels;

public class TextureGroupingTests
{
    [Theory]
    [InlineData("032/motion/eu_titleselect_eu", "Game covers (Europe)", TextureCategory.Covers)]
    [InlineData("032/motion/eu_titleselect_us", "Game covers (USA)", TextureCategory.Covers)]
    [InlineData("032/motion/eu_titleselect_jp", "Game covers (Japan)", TextureCategory.Covers)]
    [InlineData("system/motion/titleselect_ui", "Menu background and icons", TextureCategory.Interface)]
    [InlineData("system/motion/logo_m2", "M2 logo", TextureCategory.Interface)]
    [InlineData("032/motion/bg01", "Background 1", TextureCategory.Interface)]
    [InlineData("system/font/mdmini_basefont", "Font (default)", TextureCategory.Fonts)]
    [InlineData("system/font/mdmini_basefont_12", "Font 12", TextureCategory.Fonts)]
    [InlineData("system/font/mdmini_basefont_32kr", "Font 32 (Korean)", TextureCategory.Fonts)]
    [InlineData("system/font/mdmini_basefont_32tc", "Font 32 (Traditional Chinese)", TextureCategory.Fonts)]
    [InlineData("system/font/mdmini_basefont_eng", "Font (English)", TextureCategory.Fonts)]
    [InlineData("system/motion/something_new", "Something new", TextureCategory.Interface)]
    public void FilesGetAReadableTitleAndACategory(string file, string title, TextureCategory category)
    {
        Assert.Equal(title, TextureGrouping.Title(file));
        Assert.Equal(category, TextureGrouping.Classify(file));
    }

    [Fact]
    public void Leaf_HandlesBothSeparators()
    {
        Assert.Equal("b", TextureGrouping.Leaf("a/b"));
        Assert.Equal("b", TextureGrouping.Leaf(@"a\b"));
        Assert.Equal("b", TextureGrouping.Leaf("b"));
    }
}

public class RgbaImageThumbnailTests
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

public class TexturesGalleryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "m2forge-gallery-" + Guid.NewGuid().ToString("N"));

    void WriteMotion(string relative, string sourceJson, byte[] stream)
    {
        var json = Path.Combine(root, relative + ".psb.json");
        Directory.CreateDirectory(Path.GetDirectoryName(json)!);
        File.WriteAllText(json, "{\"id\":\"motion\",\"source\":" + sourceJson + "}");
        var streams = Path.Combine(root, relative + ".psb.streams");
        Directory.CreateDirectory(streams);
        File.WriteAllBytes(Path.Combine(streams, "stream_0"), stream);
    }

    static string Atlas(string name, int size, params string[] icons) =>
        "{\"" + name + "\":{\"texture\":{\"type\":\"RGBA8\",\"width\":" + size + ",\"height\":" + size + ",\"pixel\":\"_stream:0\"},\"icon\":{" +
        string.Join(',', icons.Select((icon, i) => "\"" + icon + "\":{\"left\":" + i * 2 + ",\"top\":0,\"width\":2,\"height\":2}")) + "}}}";

    TexturesViewModel Create()
    {
        WriteMotion("032/motion/eu_titleselect_eu", Atlas("tex#000", 4, "0008", "0009"), Enumerable.Repeat((byte)200, 4 * 4 * 4).ToArray());
        WriteMotion("system/motion/titleselect_ui", Atlas("tex#000", 4, "logo"), Enumerable.Repeat((byte)90, 4 * 4 * 4).ToArray());
        WriteMotion("system/font/mdmini_basefont_32kr", "[{\"type\":\"A8\",\"width\":2,\"height\":2,\"pixel\":\"_stream:0\"}]", [255, 128, 64, 0]);
        return new TexturesViewModel(new Workspace(), new FakeDialogs());
    }

    static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task LoadingBuildsOneSetPerFileInsteadOfATree()
    {
        var viewModel = Create();

        await viewModel.LoadAsync(root);

        Assert.True(viewModel.HasTextures);
        Assert.Equal(
            ["Game covers (Europe)", "Menu background and icons", "Font 32 (Korean)"],
            viewModel.Groups.Select(g => g.Title));
        Assert.Equal("Game covers (Europe)", viewModel.SelectedGroup!.Title);
        Assert.Equal("3 images", viewModel.Groups[0].Subtitle);
        Assert.StartsWith("6 images in 3 sets", viewModel.Summary);
    }

    [Fact]
    public async Task TheSelectedSetShowsItsImagesAsTilesAndLoadsTheirThumbnails()
    {
        var viewModel = Create();
        await viewModel.LoadAsync(root);

        Assert.Equal(["Image tex#000", "0008", "0009"], viewModel.Tiles.Select(t => t.Label));
        await WaitFor(() => viewModel.Tiles.All(t => t.Thumbnail is not null));
        Assert.Equal(2, viewModel.Tiles[1].Thumbnail!.Width);
        Assert.Equal(4, viewModel.Tiles[0].Thumbnail!.Width);
    }

    [Fact]
    public async Task SelectingATile_SelectsItsTextureNodeForThePreview()
    {
        var viewModel = Create();
        await viewModel.LoadAsync(root);

        viewModel.SelectedTile = viewModel.Tiles[2];

        Assert.Same(viewModel.Tiles[2].Node, viewModel.SelectedNode);
        Assert.Equal("Image 0009", viewModel.SelectionTitle);
        Assert.Equal("2 x 2", viewModel.SizeText);
        Assert.Equal("2, 0", viewModel.PositionText);
        Assert.True(viewModel.CanEditSelection);
    }

    [Fact]
    public async Task TheFirstTileIsSelectedSoThePreviewIsNeverEmpty()
    {
        var viewModel = Create();
        await viewModel.LoadAsync(root);

        Assert.Same(viewModel.Tiles[0], viewModel.SelectedTile);
        Assert.Equal("Image tex#000", viewModel.SelectionTitle);
        Assert.Equal("4 x 4", viewModel.SizeText);
        Assert.True(viewModel.CanEditSelection);
    }

    [Fact]
    public async Task TheCategoryFilterNarrowsTheSetsAndMovesTheSelection()
    {
        var viewModel = Create();
        await viewModel.LoadAsync(root);

        viewModel.SelectCategoryCommand.Execute(viewModel.Categories.Single(c => c.Category == TextureCategory.Fonts));

        Assert.Equal(["Font 32 (Korean)"], viewModel.Groups.Select(g => g.Title));
        Assert.Equal("Font 32 (Korean)", viewModel.SelectedGroup!.Title);
        Assert.True(viewModel.Categories.Single(c => c.Category == TextureCategory.Fonts).IsActive);
        Assert.False(viewModel.Categories[0].IsActive);

        viewModel.SelectCategoryCommand.Execute(viewModel.Categories[0]);
        Assert.Equal(3, viewModel.Groups.Count);
    }

    [Fact]
    public async Task SearchingFiltersTheSetsAndTheTilesInsideTheSelectedOne()
    {
        var viewModel = Create();
        await viewModel.LoadAsync(root);

        viewModel.Search = "0009";

        Assert.Equal(["Game covers (Europe)"], viewModel.Groups.Select(g => g.Title));
        Assert.Equal(["0009"], viewModel.Tiles.Select(t => t.Label));

        viewModel.Search = "korean";
        Assert.Equal(["Font 32 (Korean)"], viewModel.Groups.Select(g => g.Title));
        Assert.Single(viewModel.Tiles);
    }

    [Fact]
    public async Task ASearchWithoutResults_IsReportedAndEmptiesTheGrid()
    {
        var viewModel = Create();
        await viewModel.LoadAsync(root);

        viewModel.Search = "zzz";

        Assert.True(viewModel.IsFilteredEmpty);
        Assert.Empty(viewModel.Groups);
        Assert.Empty(viewModel.Tiles);
        Assert.False(viewModel.HasGroup);

        viewModel.Search = string.Empty;
        Assert.False(viewModel.IsFilteredEmpty);
        Assert.NotNull(viewModel.SelectedGroup);
    }

    [Fact]
    public async Task ClearEmptiesTheGallery()
    {
        var viewModel = Create();
        await viewModel.LoadAsync(root);

        viewModel.Clear();

        Assert.False(viewModel.HasTextures);
        Assert.Empty(viewModel.Groups);
        Assert.Empty(viewModel.Tiles);
        Assert.Null(viewModel.SelectedGroup);
    }

    [Fact]
    public async Task EditedImagesAreFlaggedOnTheirTileAndSet()
    {
        var workspace = new Workspace();
        var viewModel = new TexturesViewModel(workspace, new FakeDialogs());
        WriteMotion("032/motion/eu_titleselect_eu", Atlas("tex#000", 4, "0008", "0009"), Enumerable.Repeat((byte)200, 64).ToArray());
        await viewModel.LoadAsync(root);

        workspace.SetEdit(new M2Forge.Core.Projects.TextureEdit("032/motion/eu_titleselect_eu.psb", "tex#000", "0009", Path.Combine(root, "x.png")));

        Assert.True(viewModel.Tiles.Single(t => t.Label == "0009").Node.IsEdited);
        Assert.False(viewModel.Tiles.Single(t => t.Label == "0008").Node.IsEdited);
        Assert.True(viewModel.Groups[0].File.IsEdited);
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(root); attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }
}

using M2Forge.App.ViewModels;

namespace M2Forge.Tests.ViewModels;

public sealed class TextureGroupingTests
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

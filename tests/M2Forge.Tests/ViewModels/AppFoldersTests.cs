using M2Forge.App.Services;

namespace M2Forge.Tests.ViewModels;

[Collection("AppFolders")]
public sealed class AppFoldersTests : IDisposable
{
    readonly string originalRoot = AppFolders.Root;

    public AppFoldersTests()
    {
        AppFolders.Root = Path.Combine(Path.GetTempPath(), "m2forge-app-root");
    }

    [Fact]
    public void Extracted_IsNamedAfterTheFirmwareFolderAndLivesNextToTheApp()
    {
        Assert.Equal(Path.Combine(AppFolders.Root, "extracted", "032"), AppFolders.Extracted(@"Z:\fw\Backup\032"));
        Assert.Equal(Path.Combine(AppFolders.Root, "extracted", "032"), AppFolders.Extracted(@"Z:\fw\Backup\032" + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Extracted_FallsBackWhenTheFolderHasNoName()
    {
        Assert.Equal(Path.Combine(AppFolders.Root, "extracted", "firmware"), AppFolders.Extracted(string.Empty));
    }

    [Fact]
    public void Display_ShowsPathsInsideTheAppFolderRelativeToIt()
    {
        var inside = Path.Combine(AppFolders.Root, "extracted", "032");

        Assert.Equal(Path.Combine("extracted", "032"), AppFolders.Display(inside));
        Assert.Equal(Path.Combine("firmware", "MOON"), AppFolders.Display(Path.Combine(AppFolders.Root, "firmware", "MOON") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Display_KeepsPathsOutsideTheAppFolderAndHandlesEmpty()
    {
        Assert.Equal(@"C:\other-place\md2", AppFolders.Display(@"C:\other-place\md2"));
        Assert.Equal(string.Empty, AppFolders.Display(string.Empty));
    }

    [Fact]
    public void Imported_UsesTheArchiveNameWithoutItsExtension()
    {
        Assert.Equal(Path.Combine(AppFolders.Root, "firmware", "MOON-es1-v0.8.3"), AppFolders.Imported(@"Z:\fw\MOON-es1-v0.8.3.7z"));
        Assert.Equal(Path.Combine(AppFolders.Root, "firmware", "mini2-20261120"), AppFolders.Imported(@"Z:\fw\mini2\mini2-20261120.img"));
    }

    public void Dispose()
    {
        AppFolders.Root = originalRoot;
    }
}

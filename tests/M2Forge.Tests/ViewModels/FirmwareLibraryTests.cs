using System.Text;
using GMWare.M2.MArchive;
using M2Forge.App.Services;
using M2Forge.App.ViewModels;
using M2Forge.Core.Firmware;

namespace M2Forge.Tests.ViewModels;

[Collection("AppFolders")]
public class FirmwareLibraryTests : IDisposable
{
    const string Seed = "seed-for-tests";

    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-lib-" + Guid.NewGuid().ToString("N"));
    readonly string originalRoot = AppFolders.Root;
    readonly FakeDialogs dialogs = new();

    public FirmwareLibraryTests()
    {
        AppFolders.Root = Path.Combine(work, "app");
    }

    string ImportedFolder(string name) => Path.Combine(AppFolders.Root, "firmware", name);

    string ExtractedFolder(string name) => Path.Combine(AppFolders.Root, "extracted", name);

    string CreateGoodFirmware(string name)
    {
        var folder = ImportedFolder(name);
        var content = Path.Combine(work, "content-" + name);
        Directory.CreateDirectory(Path.Combine(content, "dir"));
        File.WriteAllBytes(Path.Combine(content, "one.psb"), Enumerable.Range(0, 3000).Select(i => (byte)(i % 7)).ToArray());
        File.WriteAllBytes(Path.Combine(content, "dir", "two.bin"), Enumerable.Range(0, 5000).Select(i => (byte)(i % 13)).ToArray());

        Directory.CreateDirectory(folder);
        var packer = new MArchivePacker(new ZstdSharpCodec(), Seed, 64);
        packer.CompressDirectory(content);
        AllDataPacker.Build(content, Path.Combine(folder, "alldata"), packer);
        File.WriteAllBytes(Path.Combine(folder, "m2engage"), [.. "x\0getExistFileDirInMountArchive\0\0\0"u8.ToArray(), .. Encoding.ASCII.GetBytes(Seed), 0, 7]);
        return folder;
    }

    string CreateDamagedFirmware(string name)
    {
        var folder = CreateGoodFirmware(name);
        var bin = Path.Combine(folder, "alldata.bin");
        using (var stream = new FileStream(bin, FileMode.Open, FileAccess.Write)) stream.SetLength(new FileInfo(bin).Length / 2);
        return folder;
    }

    FirmwareViewModel CreateViewModel() => new(new Workspace(), dialogs);

    static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public void Scan_ListsFoldersThatHoldFirmwareFilesAndIgnoresTheRest()
    {
        CreateGoodFirmware("alpha");
        Directory.CreateDirectory(ImportedFolder("empty"));
        Directory.CreateDirectory(ImportedFolder("partial"));
        File.WriteAllBytes(Path.Combine(ImportedFolder("partial"), "alldata.bin"), [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(ExtractedFolder("alpha"), "system"));

        var entries = FirmwareLibrary.Scan();

        Assert.Equal(["alpha", "partial"], entries.Select(e => e.Name));
        Assert.True(entries[0].IsExtracted);
        Assert.False(entries[1].IsExtracted);
        Assert.Equal(ExtractedFolder("alpha"), entries[0].ExtractedFolder);
    }

    [Fact]
    public void Scan_OfAnAppWithoutImports_IsEmpty()
    {
        Assert.Empty(FirmwareLibrary.Scan());
    }

    [Fact]
    public void TheConsoleComesFromTheSidecarOrFromTheKnownSize()
    {
        var folder = CreateGoodFirmware("alpha");
        FirmwareLibrary.WriteMeta(folder, new FirmwareMeta(@"Z:\fw\mini2.img", ConsoleKind.MegaDriveMini2, null));

        var info = FirmwareLibrary.Scan().Single();

        Assert.Equal(ConsoleKind.MegaDriveMini2, info.Console);
        Assert.Equal(@"Z:\fw\mini2.img", info.Meta!.Source);
    }

    [Fact]
    public void AnIntactFirmwareIsAssessedAndTheResultIsCached()
    {
        var folder = CreateGoodFirmware("alpha");

        var first = FirmwareLibrary.Assess(FirmwareLibrary.Scan().Single());
        var cached = FirmwareLibrary.ReadMeta(folder)!.Health!;

        Assert.Equal(FirmwareHealth.Intact, first.Health);
        Assert.Equal(FirmwareHealth.Intact, cached.Health);
        Assert.Equal(new FileInfo(Path.Combine(folder, "alldata.bin")).Length, cached.BinSize);
    }

    [Fact]
    public void AValidCache_IsUsedInsteadOfCheckingAgain()
    {
        var folder = CreateGoodFirmware("alpha");
        var info = FirmwareLibrary.Scan().Single();
        FirmwareLibrary.WriteMeta(folder, new FirmwareMeta(string.Empty, null,
            new CachedHealth(info.BinSize, info.BinTicks, FirmwareHealth.Verified, "from the cache", "Cached name")));

        var report = FirmwareLibrary.Assess(FirmwareLibrary.Scan().Single());

        Assert.Equal(FirmwareHealth.Verified, report.Health);
        Assert.Equal("from the cache", report.Detail);
    }

    [Fact]
    public void AStaleCache_IsIgnoredWhenTheFileChanged()
    {
        var folder = CreateGoodFirmware("alpha");
        var info = FirmwareLibrary.Scan().Single();
        FirmwareLibrary.WriteMeta(folder, new FirmwareMeta(string.Empty, null,
            new CachedHealth(info.BinSize, info.BinTicks, FirmwareHealth.Verified, "from the cache", null)));
        var bin = Path.Combine(folder, "alldata.bin");
        using (var stream = new FileStream(bin, FileMode.Open, FileAccess.Write)) stream.SetLength(new FileInfo(bin).Length / 2);

        var report = FirmwareLibrary.Assess(FirmwareLibrary.Scan().Single());

        Assert.Equal(FirmwareHealth.Damaged, report.Health);
    }

    [Fact]
    public void Remove_DeletesTheWorkingCopyAndItsExtraction()
    {
        var folder = CreateGoodFirmware("alpha");
        Directory.CreateDirectory(Path.Combine(ExtractedFolder("alpha"), "system"));

        FirmwareLibrary.Remove(FirmwareLibrary.Scan().Single());

        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(ExtractedFolder("alpha")));
    }

    [Fact]
    public void Remove_NeverTouchesAFolderOutsideTheAppFolder()
    {
        var outside = Path.Combine(work, "elsewhere");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "keep.txt"), [1]);
        var info = new FirmwareFolderInfo("x", outside, outside, true, 0, 0, null, null);

        FirmwareLibrary.Remove(info);

        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public async Task TheViewModelListsTheFirmwaresAndChecksThemInTheBackground()
    {
        CreateGoodFirmware("alpha");
        CreateDamagedFirmware("broken");
        var viewModel = CreateViewModel();

        await viewModel.RefreshLibraryAsync();

        Assert.True(viewModel.HasLibrary);
        Assert.Equal(["alpha", "broken"], viewModel.Library.Select(e => e.Info.Name));
        await WaitFor(() => viewModel.Library.All(e => !e.IsChecking));

        var good = viewModel.Library.Single(e => e.Info.Name == "alpha");
        var broken = viewModel.Library.Single(e => e.Info.Name == "broken");
        Assert.Equal(FirmwareHealth.Intact, good.Health);
        Assert.True(good.IsWarn);
        Assert.True(good.CanOpen);
        Assert.Equal(FirmwareHealth.Damaged, broken.Health);
        Assert.True(broken.IsBad);
        Assert.False(broken.CanOpen);
        Assert.Contains("shorter", broken.HealthDetail);
    }

    [Fact]
    public async Task OpeningAnEntry_SelectsItsFirmwareAndKeepsTheConsole()
    {
        var folder = CreateGoodFirmware("alpha");
        FirmwareLibrary.WriteMeta(folder, new FirmwareMeta(@"Z:\fw\mini2.img", ConsoleKind.MegaDriveMini2, null));
        var viewModel = CreateViewModel();
        await viewModel.RefreshLibraryAsync();
        var raised = 0;
        viewModel.Reopened += (_, _) => raised++;

        viewModel.OpenEntryCommand.Execute(viewModel.Library.Single());

        Assert.Equal(folder, viewModel.Workspace.FirmwareFolder);
        Assert.Equal(ExtractedFolder("alpha"), viewModel.Workspace.ExtractedFolder);
        Assert.Equal(ConsoleProfile.MegaDriveMini2, viewModel.Workspace.Profile);
        Assert.Equal(@"Z:\fw\mini2.img", viewModel.SourcePath);
        Assert.True(viewModel.ConsoleDetected);
        Assert.False(viewModel.ConsoleUndetected);
        Assert.True(viewModel.Library.Single().IsCurrent);
        Assert.Equal("In use", viewModel.Library.Single().OpenLabel);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task OpeningAnExtractedEntry_AsksTheShellToReloadTheExtraction()
    {
        CreateGoodFirmware("alpha");
        Directory.CreateDirectory(Path.Combine(ExtractedFolder("alpha"), "system"));
        var viewModel = CreateViewModel();
        await viewModel.RefreshLibraryAsync();
        var raised = 0;
        viewModel.Reopened += (_, _) => raised++;

        viewModel.OpenEntryCommand.Execute(viewModel.Library.Single());

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task ADamagedEntry_CannotBeOpened()
    {
        CreateDamagedFirmware("broken");
        var viewModel = CreateViewModel();
        await viewModel.RefreshLibraryAsync();
        await WaitFor(() => viewModel.Library.All(e => !e.IsChecking));

        viewModel.OpenEntryCommand.Execute(viewModel.Library.Single());

        Assert.Equal(string.Empty, viewModel.Workspace.FirmwareFolder);
    }

    [Fact]
    public async Task RemovingTheCurrentEntry_AsksFirstAndClearsTheWorkspace()
    {
        var folder = CreateGoodFirmware("alpha");
        var viewModel = CreateViewModel();
        await viewModel.RefreshLibraryAsync();
        viewModel.OpenEntryCommand.Execute(viewModel.Library.Single());
        var cleared = 0;
        viewModel.LibraryCleared += (_, _) => cleared++;

        dialogs.Confirm = false;
        await viewModel.RemoveEntryCommand.ExecuteAsync(viewModel.Library.Single());
        Assert.True(Directory.Exists(folder));

        dialogs.Confirm = true;
        await viewModel.RemoveEntryCommand.ExecuteAsync(viewModel.Library.Single());

        Assert.False(Directory.Exists(folder));
        Assert.Equal(string.Empty, viewModel.Workspace.FirmwareFolder);
        Assert.Equal(1, cleared);
        Assert.False(viewModel.HasLibrary);
    }

    public void Dispose()
    {
        AppFolders.Root = originalRoot;
        for (var attempt = 0; attempt < 50 && Directory.Exists(work); attempt++)
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }
}

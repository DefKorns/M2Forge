using M2Forge.App.Services;
using M2Forge.App.ViewModels;

namespace M2Forge.Tests.ViewModels;

[Collection("AppFolders")]
public sealed class FirmwareViewModelTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-fvm-" + Guid.NewGuid().ToString("N"));
    readonly string originalRoot = AppFolders.Root;

    public FirmwareViewModelTests()
    {
        AppFolders.Root = Path.Combine(work, "app");
    }

    string CreateFolder(bool engine, bool bin = true, bool manifest = true)
    {
        var folder = Path.Combine(work, "firmware");
        Directory.CreateDirectory(folder);
        if (manifest) File.WriteAllBytes(Path.Combine(folder, "alldata.psb.m"), [1]);
        if (bin) File.WriteAllBytes(Path.Combine(folder, "alldata.bin"), new byte[2048]);
        if (engine)
            File.WriteAllBytes(
                Path.Combine(folder, "m2engage"),
                [.. "x\0"u8.ToArray(), .. "getExistFileDirInMountArchive\0\0\0"u8.ToArray(), .. "abcDEF123"u8.ToArray(), 0, 7]);
        return folder;
    }

    static async Task WaitForVerification(FirmwareViewModel viewModel)
    {
        for (var i = 0; i < 200 && viewModel.Checks.Count < 4; i++) await Task.Delay(20);
        Assert.Equal(4, viewModel.Checks.Count);
    }

    [Fact]
    public async Task ChoosingAFolder_DetectsTheSeedAndSuggestsAnOutput()
    {
        var workspace = new Workspace();
        var viewModel = new FirmwareViewModel(workspace, new FakeDialogs());
        var folder = CreateFolder(engine: true);

        workspace.FirmwareFolder = folder;

        Assert.Equal("abcDEF123", workspace.Seed);
        Assert.Equal(Path.Combine(work, "app", "extracted", "firmware"), workspace.ExtractedFolder);
        await WaitForVerification(viewModel);
        Assert.All(viewModel.Checks.Take(3), c => Assert.True(c.Ok));
        Assert.False(viewModel.Checks[3].Ok);
        Assert.Equal("Verification", viewModel.Checks[3].Label);
        Assert.True(viewModel.CanExtract);
    }

    [Fact]
    public async Task MissingFiles_ShowAsFailedChecksAndBlockExtraction()
    {
        var workspace = new Workspace();
        var viewModel = new FirmwareViewModel(workspace, new FakeDialogs());

        workspace.FirmwareFolder = CreateFolder(engine: false, bin: false, manifest: false);

        await WaitForVerification(viewModel);
        Assert.Equal(["Archive manifest", "Archive data", "Seed", "Verification"], viewModel.Checks.Select(c => c.Label));
        Assert.All(viewModel.Checks, c => Assert.False(c.Ok));
        Assert.False(viewModel.CanExtract);
    }

    [Fact]
    public async Task TypingASeed_UnlocksExtractionWhenTheEngineIsMissing()
    {
        var workspace = new Workspace();
        var viewModel = new FirmwareViewModel(workspace, new FakeDialogs());
        workspace.FirmwareFolder = CreateFolder(engine: false);
        await WaitForVerification(viewModel);
        Assert.False(viewModel.CanExtract);

        workspace.Seed = "typed-seed";

        Assert.True(viewModel.CanExtract);
        Assert.True(viewModel.Checks.Single(c => c.Label == "Seed").Ok);
    }

    [Fact]
    public void ChangingTheFolder_ReplacesTheSeed()
    {
        var workspace = new Workspace();
        _ = new FirmwareViewModel(workspace, new FakeDialogs());
        workspace.FirmwareFolder = CreateFolder(engine: true);

        File.WriteAllBytes(
            Path.Combine(workspace.FirmwareFolder, "m2engage"),
            [.. "getExistFileDirInMountArchive\0\0\0"u8.ToArray(), .. "newSeed"u8.ToArray(), 0]);
        workspace.FirmwareFolder = workspace.FirmwareFolder + Path.DirectorySeparatorChar;

        Assert.Equal("newSeed", workspace.Seed);
    }

    [Fact]
    public async Task AChosenFolder_BecomesTheSourceAndGetsAPlainLanguageSummary()
    {
        var workspace = new Workspace();
        var viewModel = new FirmwareViewModel(workspace, new FakeDialogs());
        Assert.False(viewModel.HasSource);
        Assert.Equal("Choose your firmware to begin.", viewModel.Status);

        var folder = CreateFolder(engine: true);
        workspace.FirmwareFolder = folder;
        await WaitForVerification(viewModel);

        Assert.True(viewModel.HasSource);
        Assert.Equal(folder, viewModel.SourcePath);
        Assert.Equal("firmware", viewModel.SourceName);
        Assert.Contains(folder, viewModel.SourceAccessibleName);
        Assert.Equal("Unknown firmware version", viewModel.SummaryTitle);
        Assert.True(viewModel.SummaryWarn);
        Assert.False(viewModel.SummaryOk || viewModel.SummaryBad);
        Assert.True(viewModel.ShowSummary);
        Assert.Equal("Ready to extract.", viewModel.Status);
        Assert.Equal("The extracted files will be saved to " + Path.Combine("extracted", "firmware"), viewModel.ExtractionHint);
    }

    [Fact]
    public async Task TheConsoleChoice_IsOnlyOfferedWhenTheFirmwareDoesNotRevealIt()
    {
        var workspace = new Workspace();
        var viewModel = new FirmwareViewModel(workspace, new FakeDialogs());
        workspace.FirmwareFolder = CreateFolder(engine: true);
        await WaitForVerification(viewModel);

        Assert.False(viewModel.ConsoleDetected);
        Assert.True(viewModel.ConsoleUndetected);
        Assert.Equal(workspace.Profile.Name, viewModel.ConsoleText);
    }

    [Fact]
    public async Task TheArchiveKey_IsOnlyShownWhenItWasNotFound()
    {
        var workspace = new Workspace();
        var viewModel = new FirmwareViewModel(workspace, new FakeDialogs());

        workspace.FirmwareFolder = CreateFolder(engine: true);
        await WaitForVerification(viewModel);
        Assert.False(viewModel.SeedMissing);

        File.Delete(Path.Combine(work, "firmware", "m2engage"));
        var other = new Workspace();
        var second = new FirmwareViewModel(other, new FakeDialogs());
        other.FirmwareFolder = CreateFolder(engine: false);
        await WaitForVerification(second);

        Assert.True(second.SeedMissing);
        Assert.Equal("Cannot extract yet", second.SummaryTitle);
        Assert.True(second.SummaryBad);
        Assert.Equal("Fix the problem above to continue.", second.Status);

        other.Seed = "typed-key";
        Assert.Equal("Ready to extract.", second.Status);
    }

    [Fact]
    public async Task AFolderWithoutTheFirmwareFiles_IsExplainedAndBlocksExtraction()
    {
        var workspace = new Workspace();
        var viewModel = new FirmwareViewModel(workspace, new FakeDialogs());

        workspace.FirmwareFolder = CreateFolder(engine: false, bin: false, manifest: false);
        await WaitForVerification(viewModel);

        Assert.Equal("Firmware files not found", viewModel.SummaryTitle);
        Assert.True(viewModel.SummaryBad);
        Assert.False(viewModel.CanExtract);
    }

    [Fact]
    public async Task TheStatusGuidance_NeverOverwritesARealMessage()
    {
        var workspace = new Workspace();
        var viewModel = new FirmwareViewModel(workspace, new FakeDialogs());
        workspace.FirmwareFolder = CreateFolder(engine: true);
        await WaitForVerification(viewModel);

        viewModel.Status = "Extracted 376 files.";
        workspace.Seed = "another-key";

        Assert.Equal("Extracted 376 files.", viewModel.Status);
    }

    [Fact]
    public void TheSuggestedExtractionFolder_FollowsTheFirmwareButAChosenOneStays()
    {
        var workspace = new Workspace();
        _ = new FirmwareViewModel(workspace, new FakeDialogs());
        var first = Path.Combine(work, "first");
        var second = Path.Combine(work, "second");
        var third = Path.Combine(work, "third");
        foreach (var folder in new[] { first, second, third }) Directory.CreateDirectory(folder);

        workspace.FirmwareFolder = first;
        Assert.Equal(Path.Combine(work, "app", "extracted", "first"), workspace.ExtractedFolder);

        workspace.FirmwareFolder = second;
        Assert.Equal(Path.Combine(work, "app", "extracted", "second"), workspace.ExtractedFolder);

        var chosen = Path.Combine(work, "chosen");
        workspace.ExtractedFolder = chosen;
        workspace.FirmwareFolder = third;
        Assert.Equal(chosen, workspace.ExtractedFolder);
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

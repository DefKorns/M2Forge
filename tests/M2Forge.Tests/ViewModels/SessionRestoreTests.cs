using M2Forge.App.Services;
using M2Forge.App.ViewModels;

namespace M2Forge.Tests.ViewModels;

[Collection("AppFolders")]
public class SessionRestoreTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-session-" + Guid.NewGuid().ToString("N"));
    readonly string originalRoot = AppFolders.Root;

    SessionStore Store => new(Path.Combine(work, "session"));

    string FirmwareFolder
    {
        get
        {
            var folder = Path.Combine(work, "firmware-source");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "alldata.psb.m"), [1]);
            File.WriteAllBytes(Path.Combine(folder, "alldata.bin"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(folder, "m2engage"), [1]);
            return folder;
        }
    }

    public SessionRestoreTests()
    {
        AppFolders.Root = Path.Combine(work, "app");
    }

    static MainWindowViewModel Create(SessionStore store) => new(new FakeDialogs(), store);

    static async Task Settle(MainWindowViewModel viewModel)
    {
        for (var i = 0; i < 200 && viewModel.Firmware.Checks.Count < 4; i++) await Task.Delay(20);
        await Task.Delay(100);
    }

    [Fact]
    public async Task ASessionIsRestoredWithoutAskingAnything()
    {
        var first = Create(Store);
        var firmware = FirmwareFolder;
        var extracted = Path.Combine(work, "extracted-here");
        Directory.CreateDirectory(extracted);
        first.Workspace.FirmwareFolder = firmware;
        first.Workspace.ExtractedFolder = extracted;
        first.SelectedNav = first.NavItems[2];
        await Settle(first);

        var second = Create(Store);
        await second.RestoreAsync();

        Assert.Equal(firmware, second.Workspace.FirmwareFolder);
        Assert.Equal(extracted, second.Workspace.ExtractedFolder);
        Assert.Same(second.NavItems[2], second.SelectedNav);
        Assert.True(second.Firmware.HasSource);
        Assert.Equal(firmware, second.Firmware.SourcePath);
        Assert.Null(second.Workspace.ProjectPath);
        Assert.False(second.Workspace.IsDirty);
        Assert.Contains("Welcome back", second.Notice);
    }

    [Fact]
    public async Task TheDetectedConsoleIsRememberedSoTheChoiceIsNotOfferedAgain()
    {
        var store = Store;
        var first = Create(store);
        first.Workspace.FirmwareFolder = FirmwareFolder;
        await Settle(first);
        store.Save(first.Workspace, new SessionInfo(@"Z:\fw\mini2.img", true, 0));

        var second = Create(store);
        await second.RestoreAsync();

        Assert.True(second.Firmware.ConsoleDetected);
        Assert.False(second.Firmware.ConsoleUndetected);
        Assert.Equal(@"Z:\fw\mini2.img", second.Firmware.SourcePath);
        Assert.Equal("mini2.img", second.Firmware.SourceName);
    }

    [Fact]
    public async Task EditsSurviveTheRestart()
    {
        var first = Create(Store);
        first.Workspace.FirmwareFolder = FirmwareFolder;
        first.Workspace.SetEdit(new M2Forge.Core.Projects.TextureEdit("a.psb", "tex", null, Path.Combine(work, "logo.png")));
        await Settle(first);

        var second = Create(Store);
        await second.RestoreAsync();

        var edit = Assert.Single(second.Workspace.Edits);
        Assert.Equal("a.psb", edit.Psb);
        Assert.Equal(Path.Combine(work, "logo.png"), edit.Image);
        Assert.False(second.Workspace.IsDirty);
    }

    [Fact]
    public async Task AFirmwareThatNoLongerExistsIsForgotten()
    {
        var store = Store;
        var first = Create(store);
        first.Workspace.FirmwareFolder = Path.Combine(work, "gone");

        var second = Create(store);
        await second.RestoreAsync();

        Assert.Equal(string.Empty, second.Workspace.FirmwareFolder);
        Assert.False(second.Firmware.HasSource);
        Assert.Null(store.TryLoad());
    }

    [Fact]
    public async Task StartingANewProjectClearsTheSession()
    {
        var store = Store;
        var first = Create(store);
        first.Workspace.FirmwareFolder = FirmwareFolder;
        Assert.NotNull(store.TryLoad());

        await first.NewProjectCommand.ExecuteAsync(null);

        Assert.Null(store.TryLoad());
        var second = Create(store);
        await second.RestoreAsync();
        Assert.False(second.Firmware.HasSource);
    }

    [Fact]
    public async Task WithoutAStoreNothingIsPersistedOrRestored()
    {
        var viewModel = new MainWindowViewModel(new FakeDialogs());
        viewModel.Workspace.FirmwareFolder = FirmwareFolder;

        await viewModel.RestoreAsync();

        Assert.False(Directory.Exists(Path.Combine(work, "session")));
    }

    [Fact]
    public async Task ACorruptSessionFileIsIgnored()
    {
        var store = Store;
        var first = Create(store);
        first.Workspace.FirmwareFolder = FirmwareFolder;
        await Settle(first);
        File.WriteAllText(Path.Combine(work, "session", "session.json"), "{ not json");

        Assert.Null(store.TryLoad());
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

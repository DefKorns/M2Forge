using System.Text;
using M2Forge.App.ViewModels;

namespace M2Forge.Tests.ViewModels;

public sealed class RomsViewModelTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "m2forge-rvm-" + Guid.NewGuid().ToString("N"));
    readonly FakeDialogs dialogs = new();

    static byte[] Rom(string name, int size)
    {
        var rom = new byte[size];
        Encoding.ASCII.GetBytes("SEGA MEGA DRIVE ").CopyTo(rom, 0x100);
        Encoding.ASCII.GetBytes(name.PadRight(48)).CopyTo(rom, 0x120);
        Encoding.ASCII.GetBytes(name.PadRight(48)).CopyTo(rom, 0x150);
        new Random(size).NextBytes(rom.AsSpan(0x200));
        return rom;
    }

    async Task<RomsViewModel> CreateAsync()
    {
        var folder = Path.Combine(root, "system", "roms");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "eu_en_Alpha.bin"), Rom("ALPHA", 0x1000));
        File.WriteAllBytes(Path.Combine(folder, "eu_jp_Alpha.bin"), Rom("ALPHA", 0x3000));
        File.WriteAllBytes(Path.Combine(folder, "us_en_Bravo.bin"), Rom("BRAVO", 0x2000));
        File.WriteAllBytes(Path.Combine(folder, "jp_jp_Charlie.bin"), Rom("CHARLIE", 0x800));

        var viewModel = new RomsViewModel(new Workspace(), dialogs);
        await viewModel.LoadAsync(root);
        return viewModel;
    }

    static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task Load_ListsEveryRomAndFillsTheFilters()
    {
        var viewModel = await CreateAsync();

        Assert.True(viewModel.HasRoms);
        Assert.Equal(["Alpha", "Alpha", "Bravo", "Charlie"], viewModel.Rows.Select(r => r.Title));
        Assert.Equal(["All regions", "EU", "JP", "US"], viewModel.Regions);
        Assert.Equal(["All languages", "EN", "JP"], viewModel.Languages);
        Assert.StartsWith("4 ROMs", viewModel.Summary);
    }

    [Fact]
    public async Task Filters_NarrowTheListAndTheSummary()
    {
        var viewModel = await CreateAsync();

        viewModel.RegionFilter = "EU";
        Assert.Equal(["EU", "EU"], viewModel.Rows.Select(r => r.Region));
        Assert.StartsWith("2 of 4 ROMs", viewModel.Summary);

        viewModel.LanguageFilter = "JP";
        Assert.Single(viewModel.Rows);

        viewModel.RegionFilter = "All regions";
        viewModel.LanguageFilter = "All languages";
        viewModel.Search = "brav";
        Assert.Equal("Bravo", Assert.Single(viewModel.Rows).Title);

        viewModel.Search = "ALPHA";
        Assert.Equal(2, viewModel.Rows.Count);
    }

    [Fact]
    public async Task Sort_TogglesDirectionAndSupportsSize()
    {
        var viewModel = await CreateAsync();

        viewModel.SortCommand.Execute("title");
        Assert.Equal(["Charlie", "Bravo", "Alpha", "Alpha"], viewModel.Rows.Select(r => r.Title));
        Assert.Contains("▼", viewModel.TitleHeader);

        viewModel.SortCommand.Execute("size");
        Assert.Equal([0x800L, 0x1000, 0x2000, 0x3000], viewModel.Rows.Select(r => r.Info.Size));
        Assert.DoesNotContain("▼", viewModel.TitleHeader);
        Assert.Contains("▲", viewModel.SizeHeader);

        viewModel.SortCommand.Execute("size");
        Assert.Equal(0x3000, viewModel.Rows[0].Info.Size);
    }

    [Fact]
    public async Task Selection_CountsAcrossFiltersAndDrivesTheExportButton()
    {
        var viewModel = await CreateAsync();
        Assert.False(viewModel.ExportSelectedCommand.CanExecute(null));
        Assert.Equal("Export selected", viewModel.ExportLabel);

        viewModel.RegionFilter = "EU";
        viewModel.SelectVisibleCommand.Execute(null);
        viewModel.RegionFilter = "All regions";

        Assert.Equal(2, viewModel.SelectedCount);
        Assert.Equal("Export 2 selected", viewModel.ExportLabel);
        Assert.True(viewModel.ExportSelectedCommand.CanExecute(null));

        viewModel.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedCount);
    }

    [Fact]
    public async Task ExportSelected_WritesFilesAndReportsSkips()
    {
        var viewModel = await CreateAsync();
        dialogs.Folder = Path.Combine(root, "out");
        viewModel.SelectVisibleCommand.Execute(null);

        await viewModel.ExportSelectedCommand.ExecuteAsync(null);

        Assert.False(viewModel.StatusIsError);
        Assert.StartsWith("Exported 4 ROM(s)", viewModel.Status);
        Assert.Equal(4, Directory.GetFiles(dialogs.Folder).Length);
        Assert.True(File.Exists(Path.Combine(dialogs.Folder, "eu_jp_Alpha.bin")));

        await viewModel.ExportSelectedCommand.ExecuteAsync(null);
        Assert.Contains("4 already existed", viewModel.Status);

        viewModel.Extension = ".gen";
        await viewModel.ExportSelectedCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(dialogs.Folder, "us_en_Bravo.gen")));
    }

    [Fact]
    public async Task ExportSelected_DoesNothingWhenTheFolderDialogIsCancelled()
    {
        var viewModel = await CreateAsync();
        dialogs.Folder = null;
        viewModel.SelectVisibleCommand.Execute(null);

        await viewModel.ExportSelectedCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.Status);
    }

    [Fact]
    public async Task Current_LoadsHashesAndExportsASingleRom()
    {
        var viewModel = await CreateAsync();
        var row = viewModel.Rows.First(r => r.Info.Size == 0x2000);

        viewModel.Current = row;

        Assert.True(viewModel.HasCurrent);
        Assert.True(viewModel.HasCurrentHeader);
        await WaitFor(() => viewModel.Sha1Text.Length == 40);
        Assert.Equal(8, viewModel.CrcText.Length);

        dialogs.SaveFile = Path.Combine(root, "single", "Bravo.md");
        Directory.CreateDirectory(Path.GetDirectoryName(dialogs.SaveFile)!);
        await viewModel.ExportCurrentCommand.ExecuteAsync(null);

        Assert.Equal("us_en_Bravo", dialogs.LastSuggestedName);
        Assert.Equal(File.ReadAllBytes(row.Info.Path), File.ReadAllBytes(dialogs.SaveFile));
        Assert.Equal("Exported Bravo.md.", viewModel.Status);
    }

    [Fact]
    public async Task Clear_EmptiesThePage()
    {
        var viewModel = await CreateAsync();
        viewModel.SelectVisibleCommand.Execute(null);

        viewModel.Clear();

        Assert.False(viewModel.HasRoms);
        Assert.Empty(viewModel.Rows);
        Assert.Equal(0, viewModel.SelectedCount);
    }

    [Fact]
    public async Task Load_OfAFolderWithoutRoms_LeavesTheEmptyState()
    {
        var viewModel = new RomsViewModel(new Workspace(), dialogs);

        await viewModel.LoadAsync(root);

        Assert.False(viewModel.HasRoms);
        Assert.Empty(viewModel.Rows);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

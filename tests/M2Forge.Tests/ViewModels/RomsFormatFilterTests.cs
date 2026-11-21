using M2Forge.App.ViewModels;

namespace M2Forge.Tests.ViewModels;

public sealed class RomsFormatFilterTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "m2forge-rff-" + Guid.NewGuid().ToString("N"));
    readonly FakeDialogs dialogs = new();

    async Task<RomsViewModel> CreateAsync()
    {
        var folder = Path.Combine(root, "system", "roms");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "us_us_alpha.bin"), new byte[0x400]);
        File.WriteAllBytes(Path.Combine(folder, "jp_jp_beta.smd"), new byte[0x300]);
        File.WriteAllBytes(Path.Combine(folder, "us_us_gamma.mcd"), new byte[0x500]);

        var viewModel = new RomsViewModel(new Workspace(), dialogs);
        await viewModel.LoadAsync(root);
        return viewModel;
    }

    [Fact]
    public async Task TheFormatFilter_ListsTheFormatsPresentAndNarrowsTheRows()
    {
        var viewModel = await CreateAsync();

        Assert.Equal(["All formats", "BIN", "MCD", "SMD"], viewModel.Formats);
        Assert.Equal(3, viewModel.Rows.Count);

        viewModel.FormatFilter = "MCD";

        Assert.Equal("gamma", Assert.Single(viewModel.Rows).Title.ToLowerInvariant());
        Assert.StartsWith("1 of 3 ROMs", viewModel.Summary);
    }

    [Fact]
    public async Task SortingByType_GroupsTheFormats()
    {
        var viewModel = await CreateAsync();

        viewModel.SortCommand.Execute("format");

        Assert.Equal(["BIN", "MCD", "SMD"], viewModel.Rows.Select(r => r.Format));
        Assert.Contains("▲", viewModel.FormatHeader);
    }

    [Fact]
    public async Task ExportingOneRom_UsesItsOwnExtensionForOtherFormats()
    {
        var viewModel = await CreateAsync();
        viewModel.Current = viewModel.Rows.Single(r => r.Format == "SMD");
        dialogs.SaveFile = Path.Combine(root, "single", "Beta.smd");
        Directory.CreateDirectory(Path.GetDirectoryName(dialogs.SaveFile)!);

        await viewModel.ExportCurrentCommand.ExecuteAsync(null);

        Assert.Equal("jp_jp_beta", dialogs.LastSuggestedName);
        Assert.True(File.Exists(dialogs.SaveFile));
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

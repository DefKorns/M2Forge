using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M2Forge.App.Services;
using M2Forge.Core.Roms;
using M2Forge.Core.Textures;

namespace M2Forge.App.ViewModels;

public sealed partial class RomRow : ObservableObject
{
    readonly Action selectionChanged;
    readonly RomDetails? details;
    readonly RomArtwork? artwork;
    bool coverRequested;
    RgbaImage? cover;

    public RomRow(RomInfo info, RomDetails? details, RomArtwork? artwork, Action selectionChanged)
    {
        Info = info;
        this.details = details;
        this.artwork = artwork;
        this.selectionChanged = selectionChanged;
    }

    [ObservableProperty] bool isSelected;

    public RomInfo Info { get; }
    public string Title => Info.Title;
    public string? NativeTitle => details?.NativeTitle;
    public string Subtitle => NativeTitle ?? Info.FileName;
    public string RegionFlag => RomFlags.ForRegion(Info.Region);
    public string LanguageFlag => RomFlags.ForLanguage(Info.Language);
    public bool HasLanguageFlag => LanguageFlag.Length > 0;
    public bool IsBios => Info.FileName.Contains("bios", StringComparison.OrdinalIgnoreCase);

    public RgbaImage? Cover
    {
        get
        {
            if (!coverRequested)
            {
                coverRequested = true;
                _ = LoadCoverAsync(CoverKind.Cover, image => cover = image, nameof(Cover));
            }
            return cover;
        }
    }

    public async Task<RgbaImage?> LoadAsync(CoverKind kind)
    {
        if (artwork is null || details?.CoverEntry is not { } entry) return null;

        var set = artwork.SetForRegion(Info.Region);
        return await Task.Run(() => artwork.Get(entry, kind, set));
    }

    async Task LoadCoverAsync(CoverKind kind, Action<RgbaImage?> assign, string property)
    {
        assign(await LoadAsync(kind));
        OnPropertyChanged(property);
    }
    public string Region => Info.Region.ToUpperInvariant();
    public string Language => Info.Language.ToUpperInvariant();
    public string Format => Info.Format.ToUpperInvariant();
    public string SizeText => RomsViewModel.FormatSize(Info.Size);

    partial void OnIsSelectedChanged(bool value) => selectionChanged();
}

public partial class RomsViewModel : ViewModelBase
{
    const string AllRegions = "All regions";
    const string AllLanguages = "All languages";
    const string AllFormats = "All formats";

    readonly Workspace workspace;
    readonly IDialogService dialogs;
    List<RomRow> all = [];
    int detailVersion;
    int coverVersion;

    public RomsViewModel(Workspace workspace, IDialogService dialogs)
    {
        this.workspace = workspace;
        this.dialogs = dialogs;
        Regions.Add(AllRegions);
        Languages.Add(AllLanguages);
        Formats.Add(AllFormats);
        formatFilter = AllFormats;
        regionFilter = AllRegions;
        languageFilter = AllLanguages;
        var settings = AppSettings.Load();
        chdmanPath = settings.ChdmanPath ?? string.Empty;
        compressExports = settings.CompressExports;
    }

    IReadOnlyList<M2Forge.Core.Discs.SegaCdBiosFile> biosFiles = [];

    IReadOnlyList<M2Forge.Core.Discs.SegaCdBiosFile> BiosFiles
    {
        get => biosFiles;
        set
        {
            biosFiles = value;
            OnPropertyChanged(nameof(HasBios));
            OnPropertyChanged(nameof(BiosLabel));
        }
    }

    public bool HasBios => biosFiles.Count > 0;
    public string BiosLabel => "Export Sega CD BIOS (" + string.Join(", ", biosFiles.Select(b => b.RetroArchName)) + ")";

    [RelayCommand(CanExecute = nameof(HasBios))]
    async Task ExportBiosAsync()
    {
        if (await dialogs.PickFolderAsync("Export the Sega CD BIOS to (the RetroArch system folder)") is not { } target) return;

        var result = await Task.Run(() => M2Forge.Core.Discs.SegaCdBios.Export(biosFiles, target, Overwrite));
        StatusIsError = result.Failures.Count > 0;
        Status = result.Failures.Count > 0
            ? "Could not export the BIOS: " + result.Failures[0]
            : $"Exported {result.Exported} BIOS file(s) with the names RetroArch expects." +
              (result.Skipped > 0 ? $" {result.Skipped} already existed and were kept." : string.Empty);
    }

    [ObservableProperty] string chdmanPath;
    [ObservableProperty] bool compressExports;

    public string? ResolveChdman() =>
        ChdmanPath.Length > 0 && File.Exists(ChdmanPath) ? ChdmanPath : M2Forge.Core.Discs.Chdman.Find();

    string? ChdmanForExport() => CompressExports ? ResolveChdman() : null;

    public string ChdmanText => ResolveChdman() is { } found
        ? "Games in ZIP, CD games in CHD (chdman: " + AppFolders.Display(found) + ")"
        : "Games in ZIP. CD games need chdman to become CHD; until then they are saved as BIN/CUE.";

    partial void OnCompressExportsChanged(bool value)
    {
        var settings = AppSettings.Load();
        settings.CompressExports = value;
        settings.Save();
    }

    partial void OnChdmanPathChanged(string value)
    {
        var settings = AppSettings.Load();
        settings.ChdmanPath = value.Length > 0 ? value : null;
        settings.Save();
        OnPropertyChanged(nameof(ChdmanText));
        OnPropertyChanged(nameof(HasChdmanChoice));
    }

    public bool HasChdmanChoice => ChdmanPath.Length > 0;

    [RelayCommand]
    async Task ChooseChdmanAsync()
    {
        var pattern = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";
        if (await dialogs.OpenFileAsync("Locate chdman", "chdman", pattern) is { } file) ChdmanPath = file;
    }

    [RelayCommand]
    void ClearChdman() => ChdmanPath = string.Empty;

    public ObservableCollection<RomRow> Rows { get; } = [];
    public ObservableCollection<string> Regions { get; } = [];
    public ObservableCollection<string> Languages { get; } = [];
    public ObservableCollection<string> Formats { get; } = [];
    public IReadOnlyList<string> Extensions { get; } = [".bin", ".md", ".gen"];

    [ObservableProperty] string search = string.Empty;
    [ObservableProperty] string regionFilter;
    [ObservableProperty] string languageFilter;
    [ObservableProperty] string formatFilter;
    [ObservableProperty] string sortKey = "title";
    [ObservableProperty] bool sortDescending;
    [ObservableProperty] string extension = ".bin";
    [ObservableProperty] bool overwrite;
    [ObservableProperty] RomRow? current;
    [ObservableProperty] bool hasRoms;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] string summary = string.Empty;
    [ObservableProperty] string status = string.Empty;
    [ObservableProperty] bool statusIsError;
    [ObservableProperty] int selectedCount;
    [ObservableProperty] string crcText = string.Empty;
    [ObservableProperty] string sha1Text = string.Empty;
    [ObservableProperty] RgbaImage? currentCover;

    public bool HasStatus => Status.Length > 0;
    public bool HasCurrent => Current is not null;
    public bool HasCurrentHeader => Current?.Info.Header is not null;
    public string TitleHeader => HeaderText("Title", "title");
    public string RegionHeader => HeaderText("Region", "region");
    public string LanguageHeader => HeaderText("Lang", "language");
    public string FormatHeader => HeaderText("Type", "format");
    public string SizeHeader => HeaderText("Size", "size");
    public string ExportLabel => SelectedCount == 0 ? "Export selected" : $"Export {SelectedCount} selected";
    public string CurrentHeaderChecksum => Current?.Info.Header is { } h ? h.Checksum.ToString("X4") : string.Empty;

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnRegionFilterChanged(string value) => ApplyFilter();
    partial void OnLanguageFilterChanged(string value) => ApplyFilter();
    partial void OnFormatFilterChanged(string value) => ApplyFilter();
    partial void OnSortKeyChanged(string value) => ApplyFilter();
    partial void OnSortDescendingChanged(bool value) => ApplyFilter();
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    public bool ShowEmpty => !HasRoms && !IsBusy;
    public bool ShowLoading => !HasRoms && IsBusy;

    partial void OnHasRomsChanged(bool value) => NotifyLoadState();

    partial void OnIsBusyChanged(bool value)
    {
        ExportSelectedCommand.NotifyCanExecuteChanged();
        NotifyLoadState();
    }

    void NotifyLoadState()
    {
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowLoading));
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ExportLabel));
        ExportSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentChanged(RomRow? value)
    {
        _ = LoadCurrentCoverAsync(value);
        OnPropertyChanged(nameof(HasCurrent));
        OnPropertyChanged(nameof(HasCurrentHeader));
        OnPropertyChanged(nameof(CurrentHeaderChecksum));
        ExportCurrentCommand.NotifyCanExecuteChanged();
        _ = LoadHashesAsync(value);
    }

    public void Clear()
    {
        all = [];
        BiosFiles = [];
        Rows.Clear();
        Current = null;
        HasRoms = false;
        Summary = string.Empty;
        Status = string.Empty;
        SelectedCount = 0;
    }

    public async Task LoadAsync(string folder)
    {
        IsBusy = true;
        try
        {
            var (scanned, catalog) = await Task.Run(() => (RomLibrary.Scan(folder), RomCatalog.Load(folder)));
            var artwork = catalog.RegionFolder.Length > 0 ? new RomArtwork(catalog.RegionFolder) : null;
            var roms = scanned.Select(r => r with { Title = catalog.Find(r.FileName)?.Title ?? RomCatalog.Prettify(r.FileName) }).ToList();
            all = roms.Select(r => new RomRow(r, catalog.Find(r.FileName), artwork, UpdateSelectedCount)).ToList();
            HasRoms = all.Count > 0;
            BiosFiles = M2Forge.Core.Discs.SegaCdBios.Select(roms);
            Current = null;

            ReplaceOptions(Regions, AllRegions, roms.Select(r => r.Region.ToUpperInvariant()));
            ReplaceOptions(Languages, AllLanguages, roms.Select(r => r.Language.ToUpperInvariant()));
            ReplaceOptions(Formats, AllFormats, roms.Select(r => r.Format.ToUpperInvariant()));
            RegionFilter = AllRegions;
            LanguageFilter = AllLanguages;
            FormatFilter = AllFormats;
            Search = string.Empty;
            ApplyFilter();
            UpdateSelectedCount();
        }
        finally
        {
            IsBusy = false;
        }
    }

    static void ReplaceOptions(ObservableCollection<string> target, string first, IEnumerable<string> values)
    {
        target.Clear();
        target.Add(first);
        foreach (var value in values.Where(v => v.Length > 0).Distinct().Order(StringComparer.Ordinal))
            target.Add(value);
    }

    void UpdateSelectedCount() => SelectedCount = all.Count(r => r.IsSelected);

    void ApplyFilter()
    {
        IEnumerable<RomRow> rows = all;

        var term = Search.Trim();
        if (term.Length > 0)
            rows = rows.Where(r =>
                r.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.NativeTitle?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                r.Info.HeaderName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Info.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (RegionFilter != AllRegions) rows = rows.Where(r => r.Region == RegionFilter);
        if (LanguageFilter != AllLanguages) rows = rows.Where(r => r.Language == LanguageFilter);
        if (FormatFilter != AllFormats) rows = rows.Where(r => r.Format == FormatFilter);

        Func<RomRow, object> key = SortKey switch
        {
            "region" => r => r.Info.Region + "\u0000" + r.Info.Language,
            "language" => r => r.Info.Language + "\u0000" + r.Info.Region,
            "format" => r => r.Info.Format + "\u0000" + r.Info.Title,
            "size" => r => r.Info.Size,
            _ => r => r.Info.Title,
        };
        var comparer = SortKey == "size" ? null : (IComparer<object>)Comparer<object>.Create((a, b) => string.Compare((string)a, (string)b, StringComparison.OrdinalIgnoreCase));
        var ordered = SortDescending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);
        var visible = ordered.ThenBy(r => r.Info.Title, StringComparer.OrdinalIgnoreCase).ToList();

        Rows.Clear();
        foreach (var row in visible) Rows.Add(row);

        var bytes = visible.Sum(r => r.Info.Size);
        Summary = visible.Count == all.Count
            ? $"{all.Count} ROMs, {FormatSize(bytes)}"
            : $"{visible.Count} of {all.Count} ROMs, {FormatSize(bytes)}";

        OnPropertyChanged(nameof(TitleHeader));
        OnPropertyChanged(nameof(RegionHeader));
        OnPropertyChanged(nameof(LanguageHeader));
        OnPropertyChanged(nameof(FormatHeader));
        OnPropertyChanged(nameof(SizeHeader));
    }

    string HeaderText(string label, string key) =>
        SortKey != key ? label : label + (SortDescending ? "  ▼" : "  ▲");

    internal static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024.0):0.#} MB" : $"{bytes / 1024.0:0.#} KB";

    async Task LoadCurrentCoverAsync(RomRow? row)
    {
        var version = ++coverVersion;
        CurrentCover = null;
        if (row is null) return;

        var image = await row.LoadAsync(CoverKind.BigCover) ?? await row.LoadAsync(CoverKind.Cover);
        if (version == coverVersion) CurrentCover = image;
    }

    async Task LoadHashesAsync(RomRow? row)
    {
        var version = ++detailVersion;
        CrcText = Sha1Text = string.Empty;
        if (row is null) return;

        try
        {
            var hashes = await Task.Run(() => RomExporter.ComputeHashes(row.Info.Path));
            if (version != detailVersion) return;
            CrcText = hashes.Crc32;
            Sha1Text = hashes.Sha1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (version == detailVersion) CrcText = "unreadable";
        }
    }

    [RelayCommand]
    void Sort(string key)
    {
        if (SortKey == key) SortDescending = !SortDescending;
        else
        {
            SortDescending = false;
            SortKey = key;
        }
    }

    [RelayCommand]
    void SelectVisible()
    {
        foreach (var row in Rows) row.IsSelected = true;
    }

    [RelayCommand]
    void ClearSelection()
    {
        foreach (var row in all) row.IsSelected = false;
    }

    [RelayCommand(CanExecute = nameof(CanExportSelected))]
    async Task ExportSelectedAsync()
    {
        if (await dialogs.PickFolderAsync("Export the selected ROMs to") is not { } target) return;

        var chosen = all.Where(r => r.IsSelected).Select(r => r.Info).ToList();
        var extension = Extension;
        var overwrite = Overwrite;

        IsBusy = true;
        StatusIsError = false;
        Status = "Exporting...";
        try
        {
            var result = await RomExporter.ExportAsync(chosen, target, extension, overwrite, ChdmanForExport(), zipCartridges: CompressExports);
            StatusIsError = result.Failures.Count > 0;
            Status = $"Exported {result.Exported} ROM(s), {FormatSize(result.Bytes)}." +
                     (result.Skipped > 0 ? $" {result.Skipped} already existed and were kept." : string.Empty) +
                     (result.Failures.Count > 0 ? $" {result.Failures.Count} failed: {result.Failures[0]}" : string.Empty) +
                     (CompressExports && chosen.Any(RomExporter.IsDisc) && ResolveChdman() is null
                         ? " Discs were saved as BIN/CUE; choose chdman to get smaller CHD files."
                         : string.Empty) +
                     BiosReminder(chosen);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusIsError = true;
            Status = "Export failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    static string BiosReminder(IEnumerable<RomInfo> exported)
    {
        var names = exported.Where(RomExporter.IsDisc)
            .Select(r => M2Forge.Core.Discs.SegaCdBios.NameForRegion(r.Region))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        return names.Count == 0 ? string.Empty : $" Sega CD games need {string.Join(" / ", names)} in RetroArch's system folder (use Export Sega CD BIOS).";
    }

    bool CanExportSelected() => !IsBusy && SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    async Task ExportCurrentAsync()
    {
        if (Current is not { } row) return;

        if (RomExporter.IsDisc(row.Info))
        {
            if (await dialogs.PickFolderAsync("Export the disc to") is not { } folder) return;
            IsBusy = true;
            Status = "Converting the disc...";
            try
            {
                var result = await RomExporter.ExportAsync([row.Info], folder, Extension, Overwrite, ChdmanForExport(), zipCartridges: CompressExports);
                StatusIsError = result.Failures.Count > 0;
                Status = result.Failures.Count > 0
                    ? "Export failed: " + result.Failures[0]
                    : (result.Skipped > 0 ? "The disc already exists there and was kept." : $"Exported {Path.GetFileNameWithoutExtension(row.Info.FileName)}.") + BiosReminder([row.Info]);
            }
            finally
            {
                IsBusy = false;
            }
            return;
        }

        var exportName = RomExporter.ExportName(row.Info, Extension);
        var suggested = Path.GetFileNameWithoutExtension(exportName);
        var path = await dialogs.SaveFileAsync("Export ROM", suggested, Path.GetExtension(exportName).TrimStart('.'));
        if (path is null) return;

        try
        {
            await Task.Run(() => File.Copy(row.Info.Path, path, overwrite: true));
            StatusIsError = false;
            Status = $"Exported {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusIsError = true;
            Status = "Export failed: " + ex.Message;
        }
    }

    [RelayCommand]
    async Task OpenFolderAsync()
    {
        if (await dialogs.PickFolderAsync("Extracted firmware folder") is not { } folder) return;
        workspace.ExtractedFolder = folder;
        workspace.RequestReload();
    }
}

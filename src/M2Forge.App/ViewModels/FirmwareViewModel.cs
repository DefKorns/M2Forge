using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M2Forge.App.Services;
using M2Forge.Core.Firmware;

namespace M2Forge.App.ViewModels;

public sealed record FirmwareCheck(string Label, bool Ok, string Detail);

public partial class FirmwareViewModel : ViewModelBase
{
    readonly IDialogService dialogs;

    public FirmwareViewModel(Workspace workspace, IDialogService dialogs)
    {
        Workspace = workspace;
        this.dialogs = dialogs;
        workspace.PropertyChanged += OnWorkspaceChanged;
        Library.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLibrary));
        Evaluate();
    }

    public Workspace Workspace { get; }
    public ObservableCollection<ConsoleProfile> Profiles { get; } = new(ConsoleProfile.All);
    public ObservableCollection<FirmwareCheck> Checks { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public ObservableCollection<FirmwareEntry> Library { get; } = [];

    public bool HasLibrary => Library.Count > 0;

    public event EventHandler? Extracted;
    public event EventHandler? Reopened;
    public event EventHandler? LibraryCleared;

    const string WelcomeStatus = "Choose your firmware to begin.";
    const string ReadyStatus = "Ready to extract.";
    const string BlockedStatus = "Fix the problem above to continue.";

    [ObservableProperty] string status = WelcomeStatus;
    [ObservableProperty] string sourcePath = string.Empty;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool failed;

    ConsoleProfile? DetectedConsole { get; set; }

    public bool HasSource => SourcePath.Length > 0;
    public string SourceName => HasSource ? Path.GetFileName(SourcePath.TrimEnd('\\', '/')) : string.Empty;
    public string SourceAccessibleName => HasSource ? "Selected firmware: " + SourcePath : "No firmware selected";
    public bool ConsoleDetected => DetectedConsole is not null || (report is { Match: not null } && reportFolder == Workspace.FirmwareFolder);
    public bool ConsoleUndetected => HasSource && !IsBusy && !ConsoleDetected;
    public bool ConsolePending => HasSource && IsBusy && !ConsoleDetected;
    public string ConsoleText => Workspace.Profile.Name;
    public bool SeedMissing => HasSource && Workspace.Seed.Length == 0;
    public bool ShowSummary => HasSource && !IsBusy;
    public string SummaryTitle => Describe().Title;
    public string SummaryText => Describe().Text;
    public bool SummaryOk => Describe().Level == 0;
    public bool SummaryWarn => Describe().Level == 1;
    public bool SummaryBad => Describe().Level == 2;
    public string ExtractionHint => Workspace.ExtractedFolder.Length == 0 ? string.Empty : "The extracted files will be saved to " + AppFolders.Display(Workspace.ExtractedFolder);

    string suggestedExtraction = string.Empty;
    int libraryVersion;
    ValidationReport? report;
    string reportFolder = string.Empty;
    int validationVersion;

    public bool CanExtract => !IsBusy && FindArchive() is not null && File.Exists(BinPath) && Workspace.Seed.Length > 0 && Workspace.ExtractedFolder.Length > 0;

    string BinPath => Path.Combine(Workspace.FirmwareFolder, "alldata.bin");

    partial void OnSourcePathChanged(string value) => NotifySummary();

    partial void OnIsBusyChanged(bool value)
    {
        NotifyCanExtract();
        NotifySummary();
    }

    void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Workspace.FirmwareFolder):
                UpdateCurrentMarks();
                if (FirmwareImporter.IsArchive(Workspace.FirmwareFolder) || FirmwareImporter.IsDiskImage(Workspace.FirmwareFolder))
                {
                    _ = ImportArchiveAsync(Workspace.FirmwareFolder);
                    break;
                }
                if (!IsInsideImportedFolder(Workspace.FirmwareFolder))
                {
                    SourcePath = Workspace.FirmwareFolder;
                    DetectedConsole = null;
                }
                DetectSeed();
                Evaluate();
                _ = ValidateAsync(Workspace.FirmwareFolder);
                break;
            case nameof(Workspace.Seed):
            case nameof(Workspace.ExtractedFolder):
            case nameof(Workspace.Profile):
                Evaluate();
                break;
        }
    }

    public async Task RefreshLibraryAsync()
    {
        var version = ++libraryVersion;
        var infos = await Task.Run(FirmwareLibrary.Scan);
        if (version != libraryVersion) return;

        Library.Clear();
        foreach (var info in infos)
            Library.Add(new FirmwareEntry(info) { IsCurrent = IsCurrentFolder(info.Folder) });

        _ = VerifyLibraryAsync(version);
    }

    async Task VerifyLibraryAsync(int version)
    {
        foreach (var entry in Library.ToList())
        {
            if (version != libraryVersion) return;

            var report = await Task.Run(() => FirmwareLibrary.Assess(entry.Info));
            if (version != libraryVersion) return;

            entry.HealthDetail = report.Detail;
            entry.Health = report.Health;
        }
    }

    bool IsCurrentFolder(string folder) =>
        Workspace.FirmwareFolder.Length > 0 &&
        string.Equals(Path.GetFullPath(Workspace.FirmwareFolder).TrimEnd('\\', '/'), Path.GetFullPath(folder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    void UpdateCurrentMarks()
    {
        foreach (var entry in Library) entry.IsCurrent = IsCurrentFolder(entry.Info.Folder);
    }

    [RelayCommand]
    void OpenEntry(FirmwareEntry entry)
    {
        if (!entry.CanOpen) return;

        var info = entry.Info;
        var profile = ConsoleProfile.All.FirstOrDefault(p => p.Kind == info.Console);
        if (profile is not null) Workspace.Profile = profile;
        DetectedConsole = profile;
        SourcePath = info.Meta?.Source is { Length: > 0 } source ? source : info.Folder;

        suggestedExtraction = info.ExtractedFolder;
        Workspace.ExtractedFolder = info.ExtractedFolder;
        Workspace.FirmwareFolder = info.Folder;
        Status = WelcomeStatus;
        UpdateCurrentMarks();
        Evaluate();

        if (info.IsExtracted) Reopened?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    async Task RemoveEntryAsync(FirmwareEntry entry)
    {
        var message = $"Delete the working copy of {entry.Title} and its extracted files from the app folder? Your original firmware file is not touched.";
        if (!await dialogs.ConfirmAsync("Remove firmware", message, "Remove")) return;

        try
        {
            await Task.Run(() => FirmwareLibrary.Remove(entry.Info));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Failed = true;
            Status = "Could not remove it: " + ex.Message;
            return;
        }

        if (entry.IsCurrent)
        {
            Workspace.ExtractedFolder = string.Empty;
            Workspace.FirmwareFolder = string.Empty;
            LibraryCleared?.Invoke(this, EventArgs.Empty);
        }
        await RefreshLibraryAsync();
    }

    public void RestoreSource(string source, bool consoleDetected)
    {
        if (source.Length > 0) SourcePath = source;
        DetectedConsole = consoleDetected ? Workspace.Profile : null;
        NotifySummary();
    }

    static bool IsInsideImportedFolder(string folder)
    {
        if (folder.Length == 0) return false;

        var root = Path.GetFullPath(Path.Combine(AppFolders.Root, "firmware")).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    void DetectSeed()
    {
        var engine = Path.Combine(Workspace.FirmwareFolder, "m2engage");
        if (File.Exists(engine))
            Workspace.Seed = PsbKeyFinder.FindInFile(engine) ?? string.Empty;

        if (Workspace.FirmwareFolder.Length == 0) return;

        if (Workspace.ExtractedFolder.Length == 0 || Workspace.ExtractedFolder == suggestedExtraction)
        {
            suggestedExtraction = AppFolders.Extracted(Workspace.FirmwareFolder);
            Workspace.ExtractedFolder = suggestedExtraction;
        }
    }

    void Evaluate()
    {
        Checks.Clear();
        var folder = Workspace.FirmwareFolder;
        if (folder.Length > 0)
        {
            var archive = FindArchive();
            Checks.Add(new FirmwareCheck("Archive manifest", archive is not null, archive is null ? "alldata.psb.m not found" : Path.GetFileName(archive)));
            Checks.Add(new FirmwareCheck("Archive data", File.Exists(BinPath), File.Exists(BinPath) ? FormatSize(new FileInfo(BinPath).Length) : "alldata.bin not found"));
            var hasEngine = File.Exists(Path.Combine(folder, "m2engage"));
            Checks.Add(new FirmwareCheck(
                "Seed",
                Workspace.Seed.Length > 0,
                Workspace.Seed.Length > 0 ? (hasEngine ? $"{Workspace.Seed}, read from m2engage" : Workspace.Seed) : "m2engage not found; type the seed below"));
        }
        if (folder.Length > 0 && DetectedConsole is { } detected)
            Checks.Add(new FirmwareCheck("Console", detected == Workspace.Profile, detected == Workspace.Profile
                ? detected.Name + ", detected from the firmware"
                : $"this firmware is a {detected.Name}, but {Workspace.Profile.Name} is selected"));
        if (folder.Length > 0 && report is not null && reportFolder == folder) Checks.Add(DescribeReport(report));
        NotifyCanExtract();
        UpdateGuidance();
        NotifySummary();
    }

    void UpdateGuidance()
    {
        if (IsBusy || Status is not (WelcomeStatus or ReadyStatus or BlockedStatus)) return;

        Status = !HasSource ? WelcomeStatus : CanExtract ? ReadyStatus : BlockedStatus;
    }

    void NotifySummary()
    {
        foreach (var name in new[]
        {
            nameof(HasSource), nameof(SourceName), nameof(SourceAccessibleName), nameof(ConsoleDetected), nameof(ConsoleUndetected), nameof(ConsolePending),
            nameof(ConsoleText), nameof(SeedMissing), nameof(ShowSummary), nameof(SummaryTitle), nameof(SummaryText),
            nameof(SummaryOk), nameof(SummaryWarn), nameof(SummaryBad), nameof(ExtractionHint),
        })
            OnPropertyChanged(name);
    }

    (string Title, string Text, int Level) Describe()
    {
        if (!HasSource) return (string.Empty, string.Empty, 1);

        if (FindArchive() is null || !File.Exists(BinPath))
            return ("Firmware files not found", "This choice does not contain the firmware files. Choose the firmware file (.7z or .img) or the folder that holds alldata.psb.m and alldata.bin.", 2);

        if (Workspace.Seed.Length == 0)
            return ("Cannot extract yet", "The key needed to read this firmware was not found automatically. Open the advanced options if you have it.", 2);

        var current = report is not null && reportFolder == Workspace.FirmwareFolder ? report : null;
        if (current is { IsGenuine: true })
            return (current.Match!.Name, "Verified. These files match a known, unmodified firmware.", 0);
        if (current is { Match: not null })
            return (current.Match.Name, "These files differ from the known firmware, so they have been modified. You can still extract them.", 1);
        if (current is not null)
            return ("Unknown firmware version", "It is not in the list of known versions. You can still extract it.", 1);

        return ("Checking the files", "This takes a few seconds.", 1);
    }

    static FirmwareCheck DescribeReport(ValidationReport result)
    {
        if (result.IsGenuine) return new FirmwareCheck("Verified", true, result.Match!.Name);
        if (result.IsMissingFiles) return new FirmwareCheck("Verification", false, "files are missing");
        if (result.Match is null) return new FirmwareCheck("Verification", false, "not a known firmware; the hashes match nothing in the catalog");
        var changed = string.Join(", ", result.Files.Where(f => f.Verdict != FileVerdict.Verified).Select(f => f.Name));
        return new FirmwareCheck("Modified", false, $"{result.Match.Name}; differs in {changed}");
    }

    async Task ValidateAsync(string folder)
    {
        var version = ++validationVersion;
        report = null;
        if (folder.Length == 0 || !Directory.Exists(folder)) return;

        var result = await Task.Run(() => FirmwareValidator.Validate(folder));
        if (version != validationVersion) return;
        report = result;
        reportFolder = folder;
        if (result.Match is { } match && ConsoleProfile.All.FirstOrDefault(p => p.Kind == match.Console) is { } profile)
            Workspace.Profile = profile;
        Evaluate();
    }

    async Task ImportArchiveAsync(string archive)
    {
        var folder = AppFolders.Imported(archive);
        SourcePath = archive;
        IsBusy = true;
        Failed = false;
        Log.Clear();
        Status = "Reading " + Path.GetFileName(archive) + "...";
        try
        {
            var progress = new Progress<string>(line =>
            {
                Log.Add(line);
                Status = FriendlyProgress(line);
            });
            var isImage = FirmwareImporter.IsDiskImage(archive);
            if (isImage) await DetectEarlyAsync(archive);
            if (!isImage)
            {
                var known = await Task.Run(() => FirmwareValidator.IdentifyArchive(FirmwareValidator.Sha256(archive)));
                Log.Add(known is null ? "Archive hash is not in the catalog." : "Archive matches " + known.Name);
            }
            var imported = await Task.Run(() => isImage
                ? FirmwareImporter.ImportDiskImage(archive, folder, progress)
                : FirmwareImporter.Import(archive, folder, progress));
            if (ConsoleProfile.All.FirstOrDefault(p => p.Kind == imported.Console) is { } detected)
            {
                if (detected != Workspace.Profile) Log.Add($"Console set to {detected.Name}, as found in the firmware.");
                Workspace.Profile = detected;
                DetectedConsole = detected;
            }
            FirmwareLibrary.WriteMeta(folder, new FirmwareMeta(archive, imported.Console, null));
            Status = WelcomeStatus;
            Workspace.FirmwareFolder = folder;
            await RefreshLibraryAsync();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Failed = true;
            Status = "Could not read the archive: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    static string FriendlyProgress(string line) =>
        line switch
        {
            _ when line.StartsWith("Copying ", StringComparison.Ordinal) => line + ". This can take a few minutes.",
            _ when line.StartsWith("Looking at", StringComparison.Ordinal) || line.StartsWith("Found", StringComparison.Ordinal) => "Looking for the firmware files...",
            _ => line,
        };

    async Task DetectEarlyAsync(string image)
    {
        try
        {
            var kind = await Task.Run(() => FirmwareImporter.DetectDiskImage(image));
            if (kind is { } found && ConsoleProfile.All.FirstOrDefault(p => p.Kind == found) is { } profile)
            {
                Workspace.Profile = profile;
                DetectedConsole = profile;
                NotifySummary();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Add("Could not read the partitions yet: " + ex.Message);
        }
    }

    void NotifyCanExtract()
    {
        OnPropertyChanged(nameof(CanExtract));
        ExtractCommand.NotifyCanExecuteChanged();
    }

    string? FindArchive()
    {
        if (Workspace.FirmwareFolder.Length == 0) return null;
        foreach (var name in new[] { "alldata.psb.m", "alldata.psb" })
        {
            var path = Path.Combine(Workspace.FirmwareFolder, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    static string FormatSize(long bytes) => bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024.0):0.#} MB" : $"{bytes / 1024.0:0.#} KB";

    [RelayCommand]
    async Task BrowseFirmwareAsync()
    {
        if (await dialogs.PickFolderAsync("Firmware folder") is { } folder)
        {
            SourcePath = folder;
            Workspace.FirmwareFolder = folder;
        }
    }

    [RelayCommand]
    async Task BrowseArchiveAsync()
    {
        if (await dialogs.OpenFileAsync("Firmware archive or disk image", "Firmware dump", "*.7z", "*.img") is { } file) await ImportArchiveAsync(file);
    }

    [RelayCommand]
    async Task BrowseOutputAsync()
    {
        if (await dialogs.PickFolderAsync("Extraction folder") is { } folder) Workspace.ExtractedFolder = folder;
    }

    [RelayCommand(CanExecute = nameof(CanExtract))]
    async Task ExtractAsync()
    {
        var archive = FindArchive()!;
        var profile = Workspace.Profile;
        var seed = Workspace.Seed;
        var output = Workspace.ExtractedFolder;

        IsBusy = true;
        Failed = false;
        Log.Clear();
        Status = "Extracting...";
        try
        {
            var progress = new Progress<string>(line => Log.Add(line));
            var result = await Task.Run(() => new FirmwareExtractor(profile, seed).Extract(archive, output, progress));
            foreach (var failure in result.Failures) Log.Add("FAILED " + failure);

            Failed = result.Failures.Count > 0;
            Status = result.Failures.Count == 0
                ? $"Extracted {result.FilesExtracted} files and {result.PsbsDeserialized} PSB files."
                : $"Extracted with {result.Failures.Count} failures; see the log.";
            Extracted?.Invoke(this, EventArgs.Empty);
            await RefreshLibraryAsync();
        }
        catch (Exception ex)
        {
            Failed = true;
            Status = "Extraction failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

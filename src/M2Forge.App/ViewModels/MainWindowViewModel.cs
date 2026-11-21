using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using M2Forge.App.Services;

namespace M2Forge.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    readonly IDialogService dialogs;
    readonly SessionStore? sessions;
    bool restoring;

    public MainWindowViewModel(IDialogService dialogs, SessionStore? sessions = null)
    {
        this.dialogs = dialogs;
        this.sessions = sessions;
        Workspace = new Workspace();
        Firmware = new FirmwareViewModel(Workspace, dialogs);
        Textures = new TexturesViewModel(Workspace, dialogs);
        Roms = new RomsViewModel(Workspace, dialogs);
        Build = new BuildViewModel(Workspace, dialogs);

        NavItems =
        [
            new NavItem("Firmware", "Extract the console files", MaterialIconKind.Chip, Firmware),
            new NavItem("Textures", "Browse and replace images", MaterialIconKind.ImageMultipleOutline, Textures),
            new NavItem("ROMs", "Browse and export the games", MaterialIconKind.GamepadVariantOutline, Roms),
            new NavItem("Build", "Check and create the new files", MaterialIconKind.Hammer, Build),
        ];
        selectedNav = NavItems[0];

        Firmware.Extracted += async (_, _) =>
        {
            SelectedNav = NavItems[1];
            await LoadExtractedAsync();
        };
        Firmware.Reopened += async (_, _) =>
        {
            SelectedNav = NavItems[1];
            await LoadExtractedAsync();
        };
        Firmware.LibraryCleared += (_, _) =>
        {
            Textures.Clear();
            Roms.Clear();
        };
        Workspace.ReloadRequested += async (_, _) => await LoadExtractedAsync();
        Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Workspace.ProjectName) or nameof(Workspace.IsDirty))
                OnPropertyChanged(nameof(ProjectTitle));
            if (e.PropertyName is nameof(Workspace.FirmwareFolder) or nameof(Workspace.ExtractedFolder) or nameof(Workspace.Seed) or nameof(Workspace.Profile))
                PersistSession();
        };
        Workspace.EditsChanged += (_, _) => PersistSession();
        Firmware.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FirmwareViewModel.SourcePath) or nameof(FirmwareViewModel.ConsoleDetected))
                PersistSession();
        };
    }

    void PersistSession()
    {
        if (sessions is null || restoring) return;

        try
        {
            if (Workspace.FirmwareFolder.Length == 0 && Workspace.Edits.Count == 0)
                sessions.Clear();
            else
                sessions.Save(Workspace, new SessionInfo(Firmware.SourcePath, Firmware.ConsoleDetected, Array.IndexOf(NavItems.ToArray(), SelectedNav)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task RestoreAsync()
    {
        if (sessions?.TryLoad() is not { } info)
        {
            await Firmware.RefreshLibraryAsync();
            return;
        }

        restoring = true;
        try
        {
            sessions.Restore(Workspace);
            if (Workspace.FirmwareFolder.Length == 0 || !Directory.Exists(Workspace.FirmwareFolder))
            {
                Workspace.Reset();
                sessions.Clear();
                return;
            }

            Firmware.RestoreSource(info.Source, info.ConsoleDetected);
            var name = Firmware.SourceName.Length > 0 ? Firmware.SourceName : "your last firmware";
            if (Workspace.ExtractedFolder.Length > 0 && Directory.Exists(Workspace.ExtractedFolder))
                await LoadExtractedAsync();

            Notice = "Welcome back. Continuing with " + name + ".";
            SelectedNav = NavItems[Math.Clamp(info.Page, 0, NavItems.Count - 1)];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or System.Text.Json.JsonException)
        {
            sessions.Clear();
        }
        finally
        {
            restoring = false;
            PersistSession();
            await Firmware.RefreshLibraryAsync();
        }
    }

    public Workspace Workspace { get; }
    public FirmwareViewModel Firmware { get; }
    public TexturesViewModel Textures { get; }
    public RomsViewModel Roms { get; }
    public BuildViewModel Build { get; }
    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty] NavItem selectedNav;
    [ObservableProperty] string notice = string.Empty;

    public ViewModelBase CurrentPage => SelectedNav.Page;
    public string PageTitle => SelectedNav.Title;
    public string PageSubtitle => SelectedNav.Subtitle;
    public string ProjectTitle => Workspace.IsDirty ? Workspace.ProjectName + " *" : Workspace.ProjectName;

    partial void OnSelectedNavChanged(NavItem value)
    {
        PersistSession();
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    async Task<bool> LoadExtractedAsync()
    {
        var folder = Workspace.ExtractedFolder;
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            Notice = "The extracted folder does not exist: " + folder;
            return false;
        }

        try
        {
            await Task.WhenAll(Textures.LoadAsync(folder), Roms.LoadAsync(folder));
            Notice = $"Loaded the extracted files: {Roms.Rows.Count} ROMs.";
            return true;
        }
        catch (Exception ex)
        {
            Notice = "Could not load the extracted files: " + ex.Message;
            return false;
        }
    }

    async Task<bool> CanDiscardAsync() =>
        !Workspace.IsDirty || await dialogs.ConfirmAsync("Unsaved changes", "The project has changes that are not saved. Discard them?", "Discard");

    [RelayCommand]
    void SelectPage(string index)
    {
        if (int.TryParse(index, out var page) && page >= 0 && page < NavItems.Count) SelectedNav = NavItems[page];
    }

    [RelayCommand]
    async Task NewProjectAsync()
    {
        if (!await CanDiscardAsync()) return;

        Workspace.Reset();
        Textures.Clear();
        Roms.Clear();
        Notice = "New project.";
        SelectedNav = NavItems[0];
    }

    [RelayCommand]
    async Task OpenProjectAsync()
    {
        if (!await CanDiscardAsync()) return;
        if (await dialogs.OpenFileAsync("Open project", "M2Forge projects", "*.m2forge.json", "*.json") is not { } path) return;

        try
        {
            Workspace.Load(path);
            Notice = $"Opened {Workspace.ProjectName}.";
            if (Workspace.ExtractedFolder.Length > 0 && Directory.Exists(Workspace.ExtractedFolder))
            {
                await LoadExtractedAsync();
                SelectedNav = NavItems[1];
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or System.Text.Json.JsonException)
        {
            Notice = "Cannot open that project: " + ex.Message;
        }
    }

    [RelayCommand]
    async Task SaveProjectAsync()
    {
        var path = Workspace.ProjectPath;
        if (path is null)
        {
            path = await dialogs.SaveFileAsync("Save project", "project.m2forge", "json");
            if (path is null) return;
            if (!path.EndsWith(".m2forge.json", StringComparison.OrdinalIgnoreCase))
                path = Path.ChangeExtension(path, null) + ".m2forge.json";
        }

        try
        {
            Workspace.Save(path);
            Notice = $"Saved {Workspace.ProjectName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice = "Cannot save: " + ex.Message;
        }
    }
}

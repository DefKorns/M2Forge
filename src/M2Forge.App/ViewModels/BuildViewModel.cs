using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M2Forge.App.Services;
using M2Forge.Core.Projects;

namespace M2Forge.App.ViewModels;

public sealed partial class BuildViewModel : ViewModelBase
{
    readonly Workspace workspace;
    readonly IDialogService dialogs;

    public BuildViewModel(Workspace workspace, IDialogService dialogs)
    {
        this.workspace = workspace;
        this.dialogs = dialogs;
        workspace.EditsChanged += (_, _) => Refresh();
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Workspace.FirmwareFolder) or nameof(Workspace.Seed) or nameof(Workspace.ExtractedFolder))
            {
                OnPropertyChanged(nameof(HasWork));
                NotifyCanBuild();
            }
        };
        Refresh();
    }

    public Workspace Workspace => workspace;
    public ObservableCollection<EditRow> Rows { get; } = [];
    public ObservableCollection<ChangedFileRow> ChangedFiles { get; } = [];
    public ObservableCollection<string> Issues { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty] string outputFolder = string.Empty;
    [ObservableProperty] string status = string.Empty;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool succeeded;
    [ObservableProperty] bool hasChecked;
    [ObservableProperty] string builtFolder = string.Empty;

    public bool HasRows => Rows.Count > 0;
    public bool HasChangedFiles => ChangedFiles.Count > 0;
    public bool HasWork => Rows.Count > 0 || Workspace.ExtractedFolder.Length > 0;
    public bool HasIssues => Issues.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
    public bool HasStatus => Status.Length > 0;
    public string EditCountText => Rows.Count == 1 ? "1 edit" : $"{Rows.Count} edits";
    public bool CanBuild => !IsBusy && HasWork && Workspace.FirmwareFolder.Length > 0 && OutputFolder.Length > 0;

    partial void OnOutputFolderChanged(string value) => NotifyCanBuild();
    partial void OnIsBusyChanged(bool value) => NotifyCanBuild();
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    void Refresh()
    {
        Rows.Clear();
        foreach (var edit in workspace.Edits)
        {
            Rows.Add(new EditRow(
                edit,
                edit.Icon is null ? edit.Texture : $"{edit.Texture} / {edit.Icon}",
                edit.Psb,
                Path.GetFileName(edit.Image),
                File.Exists(edit.Image)));
        }

        Issues.Clear();
        Warnings.Clear();
        ChangedFiles.Clear();
        OnPropertyChanged(nameof(HasChangedFiles));
        OnPropertyChanged(nameof(HasWork));
        HasChecked = false;
        Succeeded = false;
        Status = string.Empty;
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(EditCountText));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasWarnings));
        NotifyCanBuild();
    }

    void NotifyCanBuild()
    {
        OnPropertyChanged(nameof(CanBuild));
        CheckCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    void Remove(EditRow row) => workspace.RemoveEdit(row.Edit.Psb, row.Edit.Texture, row.Edit.Icon);

    [RelayCommand]
    async Task BrowseOutputAsync()
    {
        if (await dialogs.PickFolderAsync("Folder for the new firmware files") is { } folder) OutputFolder = folder;
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    async Task CheckAsync()
    {
        IsBusy = true;
        Status = "Looking for changes...";
        try
        {
            var project = workspace.ToProject();
            var plan = await Task.Run(() => ProjectBuilder.Plan(project));
            ShowPlan(plan);
            Status = Describe(plan);
        }
        catch (Exception ex)
        {
            Status = "Cannot check: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    async Task BuildAsync()
    {
        var output = OutputFolder;
        if (SameFolder(output, workspace.FirmwareFolder))
        {
            Status = "Choose a folder different from the original firmware.";
            return;
        }

        IsBusy = true;
        Succeeded = false;
        Log.Clear();
        Status = "Building...";
        try
        {
            var project = workspace.ToProject();
            var progress = new Progress<string>(line => Log.Add(line));
            var plan = await Task.Run(() => ProjectBuilder.Plan(project, progress));
            ShowPlan(plan);
            if (plan.CanBuild && plan.Replacements.Count == 0)
            {
                Status = "Nothing changed: there is nothing to pack.";
                return;
            }

            var result = await Task.Run(() => ProjectBuilder.Build(project, plan, output, progress));

            if (result.Archive is { } archive)
            {
                Succeeded = true;
                BuiltFolder = output;
                Status = $"Built {archive.Replaced} file(s), alldata.bin is {archive.BinLength / (1024.0 * 1024.0):0.#} MB.";
            }
            else
            {
                Status = $"Nothing was built: {result.Plan.Issues.Count} problem(s) to fix.";
            }
        }
        catch (Exception ex)
        {
            Status = "Build failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    void OpenOutput()
    {
        if (BuiltFolder.Length > 0 && Directory.Exists(BuiltFolder))
            Process.Start(new ProcessStartInfo(BuiltFolder) { UseShellExecute = true });
    }

    void ShowPlan(ProjectPlan plan)
    {
        Issues.Clear();
        Warnings.Clear();
        ChangedFiles.Clear();
        foreach (var change in plan.Tree)
            ChangedFiles.Add(new ChangedFileRow(change.RelativePath, $"{FormatSize(change.OldSize)} to {FormatSize(change.NewSize)}"));
        OnPropertyChanged(nameof(HasChangedFiles));
        foreach (var issue in plan.Issues) Issues.Add(issue);
        foreach (var warning in plan.Warnings) Warnings.Add(warning);
        HasChecked = true;
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasWarnings));
    }

    static string Describe(ProjectPlan plan)
    {
        if (!plan.CanBuild) return $"{plan.Issues.Count} problem(s) to fix before building.";
        var changes = plan.Replacements.Count;
        if (changes == 0) return "Nothing changed.";
        var text = changes == 1 ? "1 changed file will be packed." : $"{changes} changed files will be packed.";
        return plan.Warnings.Count == 0 ? text : $"{text} {plan.Warnings.Count} warning(s).";
    }

    static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024.0):0.#} MB" : $"{bytes / 1024.0:0.#} KB";

    static bool SameFolder(string a, string b) =>
        a.Length > 0 && b.Length > 0 &&
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}

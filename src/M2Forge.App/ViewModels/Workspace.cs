using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using M2Forge.Core.Firmware;
using M2Forge.Core.Projects;

namespace M2Forge.App.ViewModels;

public sealed partial class Workspace : ObservableObject
{
    const string ProjectSuffix = ".m2forge.json";

    [ObservableProperty] ConsoleProfile profile = ConsoleProfile.MegaDriveMini;
    [ObservableProperty] string firmwareFolder = string.Empty;
    [ObservableProperty] string extractedFolder = string.Empty;
    [ObservableProperty] string seed = string.Empty;
    [ObservableProperty] string? projectPath;
    [ObservableProperty] bool isDirty;

    public ObservableCollection<TextureEdit> Edits { get; } = [];

    public event EventHandler? EditsChanged;
    public event EventHandler? ReloadRequested;

    public void RequestReload() => ReloadRequested?.Invoke(this, EventArgs.Empty);

    public string ProjectName
    {
        get
        {
            if (ProjectPath is null) return "Untitled project";
            var name = Path.GetFileName(ProjectPath);
            return name.EndsWith(ProjectSuffix, StringComparison.OrdinalIgnoreCase)
                ? name[..^ProjectSuffix.Length]
                : Path.GetFileNameWithoutExtension(name);
        }
    }

    partial void OnProjectPathChanged(string? value) => OnPropertyChanged(nameof(ProjectName));

    public TextureEdit? FindEdit(string psb, string texture, string? icon) =>
        Edits.FirstOrDefault(e => e.Psb == psb && e.Texture == texture && e.Icon == icon);

    public void SetEdit(TextureEdit edit)
    {
        var index = Edits.ToList().FindIndex(e => e.Psb == edit.Psb && e.Texture == edit.Texture && e.Icon == edit.Icon);
        if (index >= 0) Edits[index] = edit;
        else Edits.Add(edit);
        Touch();
    }

    public bool RemoveEdit(string psb, string texture, string? icon)
    {
        var existing = FindEdit(psb, texture, icon);
        if (existing is null) return false;

        Edits.Remove(existing);
        Touch();
        return true;
    }

    public void Reset()
    {
        Edits.Clear();
        ProjectPath = null;
        FirmwareFolder = string.Empty;
        ExtractedFolder = string.Empty;
        Seed = string.Empty;
        Profile = ConsoleProfile.MegaDriveMini;
        IsDirty = false;
        EditsChanged?.Invoke(this, EventArgs.Empty);
    }

    public EditProject ToProject() => new()
    {
        Console = Profile.Kind,
        Firmware = FirmwareFolder,
        Extracted = ExtractedFolder.Length > 0 ? ExtractedFolder : null,
        Seed = FirmwareFolder.Length > 0 && File.Exists(Path.Combine(FirmwareFolder, "m2engage")) ? null : NullIfEmpty(Seed),
        Edits = [.. Edits],
    };

    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        WriteProject(full);
        ProjectPath = full;
        IsDirty = false;
    }

    public void SaveSession(string path) => WriteProject(Path.GetFullPath(path));

    void WriteProject(string full)
    {
        var project = ToProject();
        project.BaseDirectory = Path.GetDirectoryName(full)!;

        project.Firmware = project.RelativeToProject(project.Firmware);
        if (project.Extracted is not null) project.Extracted = project.RelativeToProject(project.Extracted);
        project.Edits = [.. project.Edits.Select(e => e with { Image = project.RelativeToProject(e.Image) })];

        project.Save(full);
    }

    public void Load(string path) => Apply(EditProject.Load(path), Path.GetFullPath(path));

    public void Restore(string path) => Apply(EditProject.Load(path), null);

    void Apply(EditProject project, string? projectPath)
    {
        Edits.Clear();
        ProjectPath = projectPath;
        Profile = ConsoleProfile.All.FirstOrDefault(p => p.Kind == project.Console) ?? ConsoleProfile.MegaDriveMini;
        FirmwareFolder = project.Firmware.Length > 0 ? project.Resolve(project.Firmware) : string.Empty;
        Seed = project.Seed ?? Seed;
        foreach (var edit in project.Edits)
            Edits.Add(edit with { Image = project.Resolve(edit.Image) });
        ExtractedFolder = project.Extracted is { Length: > 0 } extracted ? project.Resolve(extracted) : string.Empty;

        IsDirty = false;
        EditsChanged?.Invoke(this, EventArgs.Empty);
    }

    void Touch()
    {
        IsDirty = true;
        EditsChanged?.Invoke(this, EventArgs.Empty);
    }

    static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}

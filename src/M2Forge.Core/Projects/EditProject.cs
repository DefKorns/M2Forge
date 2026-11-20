using System.Text.Json;
using System.Text.Json.Serialization;
using M2Forge.Core.Firmware;

namespace M2Forge.Core.Projects;

public sealed record TextureEdit(string Psb, string Texture, string? Icon, string Image);

public sealed class EditProject
{
    public const int CurrentVersion = 1;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Version { get; set; } = CurrentVersion;
    public ConsoleKind Console { get; set; } = ConsoleKind.MegaDriveMini;
    public string Firmware { get; set; } = string.Empty;
    public string? Seed { get; set; }
    public string? Extracted { get; set; }
    public List<TextureEdit> Edits { get; set; } = [];

    [JsonIgnore]
    public string BaseDirectory { get; set; } = string.Empty;

    public static EditProject Load(string path)
    {
        var full = Path.GetFullPath(path);
        var project = JsonSerializer.Deserialize<EditProject>(File.ReadAllText(full), Options)
            ?? throw new InvalidDataException("The project file is empty.");

        if (project.Version != CurrentVersion)
            throw new NotSupportedException($"Project version {project.Version} is not supported (expected {CurrentVersion}).");

        project.BaseDirectory = Path.GetDirectoryName(full)!;
        return project;
    }

    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        BaseDirectory = Path.GetDirectoryName(full)!;
        var json = JsonSerializer.Serialize(this, Options).Replace("\r\n", "\n") + "\n";
        File.WriteAllText(full, json, new System.Text.UTF8Encoding(false));
    }

    public string Resolve(string path) =>
        Path.IsPathRooted(path) || BaseDirectory.Length == 0
            ? path
            : Path.GetFullPath(Path.Combine(BaseDirectory, path));

    public string RelativeToProject(string path)
    {
        if (BaseDirectory.Length == 0) return path;

        var relative = Path.GetRelativePath(BaseDirectory, path);
        return Path.IsPathRooted(relative) ? path : relative.Replace('\\', '/');
    }

    public void AddOrReplace(TextureEdit edit)
    {
        var index = Edits.FindIndex(e => Targets(e, edit));
        if (index >= 0) Edits[index] = edit;
        else Edits.Add(edit);
    }

    public bool Remove(string psb, string texture, string? icon) =>
        Edits.RemoveAll(e => e.Psb == psb && e.Texture == texture && e.Icon == icon) > 0;

    static bool Targets(TextureEdit a, TextureEdit b) => a.Psb == b.Psb && a.Texture == b.Texture && a.Icon == b.Icon;
}

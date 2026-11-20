using System;
using System.IO;
using System.Text;
using System.Text.Json;
using M2Forge.App.ViewModels;

namespace M2Forge.App.Services;

public sealed record SessionInfo(string Source, bool ConsoleDetected, int Page);

public sealed class SessionStore(string folder)
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    static readonly object Gate = new();

    public string ProjectFile => Path.Combine(folder, "session.m2forge.json");

    string InfoFile => Path.Combine(folder, "session.json");

    public void Save(Workspace workspace, SessionInfo info)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(folder);

            var projectTemp = ProjectFile + ".tmp";
            workspace.SaveSession(projectTemp);
            File.Move(projectTemp, ProjectFile, overwrite: true);

            var infoTemp = InfoFile + ".tmp";
            File.WriteAllText(infoTemp, JsonSerializer.Serialize(info, Options).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
            File.Move(infoTemp, InfoFile, overwrite: true);
        }
    }

    public SessionInfo? TryLoad()
    {
        lock (Gate)
        {
            if (!File.Exists(InfoFile) || !File.Exists(ProjectFile)) return null;

            try
            {
                return JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(InfoFile), Options);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return null;
            }
        }
    }

    public void Restore(Workspace workspace)
    {
        lock (Gate) workspace.Restore(ProjectFile);
    }

    public void Clear()
    {
        lock (Gate)
        {
            foreach (var file in new[] { ProjectFile, InfoFile, ProjectFile + ".tmp", InfoFile + ".tmp" })
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

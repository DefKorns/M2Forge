using System;
using System.IO;
using System.Text.Json;

namespace M2Forge.App.Services;

public sealed class AppSettings
{
    public string? ChdmanPath { get; set; }

    public bool CompressExports { get; set; }

    static string FilePath => Path.Combine(AppFolders.Root, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

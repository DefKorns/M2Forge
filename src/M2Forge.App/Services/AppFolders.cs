using System;
using System.IO;

namespace M2Forge.App.Services;

public static class AppFolders
{
    public static string Root { get; set; } = AppContext.BaseDirectory;

    public static string Extracted(string firmwareFolder) =>
        Path.Combine(Root, "extracted", NameOf(firmwareFolder));

    public static string Imported(string archivePath) =>
        Path.Combine(Root, "firmware", Path.GetFileNameWithoutExtension(archivePath));

    public static string Display(string path)
    {
        if (path.Length == 0) return path;

        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(Root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..].TrimEnd('\\', '/') : full;
    }

    static string NameOf(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : "firmware";
    }
}

namespace M2Forge.Core.Textures;

public static class TextureLibrary
{
    const string Suffix = ".psb.json";

    public static TextureLibraryScan Scan(string extractedRoot)
    {
        var files = new List<TextureFile>();
        var failures = new List<string>();

        var paths = Directory.EnumerateFiles(extractedRoot, "*" + Suffix, SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            try
            {
                var sources = TextureCatalog.Read(path);
                if (sources.Count == 0) continue;

                var relative = Path.GetRelativePath(extractedRoot, path)[..^Suffix.Length].Replace('\\', '/');
                files.Add(new TextureFile(relative, sources));
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.Message}");
            }
        }
        return new TextureLibraryScan(files, failures);
    }
}

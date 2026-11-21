namespace M2Forge.Core.Textures;

public static class TextureExporter
{
    public static TextureExportResult ExportFile(string psbJsonPath, string outputDir)
    {
        var textures = 0;
        var icons = 0;
        var failures = new List<string>();

        foreach (var source in TextureCatalog.Read(psbJsonPath))
        {
            try
            {
                var image = source.Load();
                Directory.CreateDirectory(outputDir);
                PngEncoder.Save(image, Path.Combine(outputDir, source.Name + ".png"));
                textures++;

                if (source.Icons.Count == 0) continue;

                var iconDir = Path.Combine(outputDir, source.Name);
                Directory.CreateDirectory(iconDir);
                foreach (var icon in source.Icons)
                {
                    PngEncoder.Save(image.Crop(icon.Left, icon.Top, icon.Width, icon.Height), Path.Combine(iconDir, icon.Name + ".png"));
                    icons++;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{psbJsonPath} [{source.Name}]: {ex.Message}");
            }
        }
        return new TextureExportResult(textures, icons, failures);
    }

    public static TextureExportResult ExportTree(string extractedRoot, string outputRoot, IProgress<string>? progress = null)
    {
        var textures = 0;
        var icons = 0;
        var failures = new List<string>();

        foreach (var file in Directory.EnumerateFiles(extractedRoot, "*.psb.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(extractedRoot, file);
            var target = Path.Combine(outputRoot, relative[..^".psb.json".Length]);
            try
            {
                var result = ExportFile(file, target);
                if (result.Textures > 0) progress?.Report($"Textures {relative}: {result.Textures}");
                textures += result.Textures;
                icons += result.Icons;
                failures.AddRange(result.Failures);
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.Message}");
            }
        }
        return new TextureExportResult(textures, icons, failures);
    }
}

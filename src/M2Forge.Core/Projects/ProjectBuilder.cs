using M2Forge.Core.Firmware;
using M2Forge.Core.Psb;
using M2Forge.Core.Textures;

namespace M2Forge.Core.Projects;

public static class ProjectBuilder
{
    public static ProjectBuildResult Build(EditProject project, string outputDirectory, IProgress<string>? progress = null)
    {
        var (archive, codec) = OpenFirmware(project);
        var plan = Plan(project, archive, codec, progress);
        if (!plan.CanBuild) return new ProjectBuildResult(plan, null);

        var built = archive.Build(outputDirectory, plan.Replacements, progress);
        return new ProjectBuildResult(plan, built);
    }

    public static ProjectBuildResult Build(EditProject project, ProjectPlan plan, string outputDirectory, IProgress<string>? progress = null)
    {
        if (!plan.CanBuild) return new ProjectBuildResult(plan, null);

        var (archive, _) = OpenFirmware(project);
        return new ProjectBuildResult(plan, archive.Build(outputDirectory, plan.Replacements, progress));
    }

    public static ProjectPlan Plan(EditProject project, IProgress<string>? progress = null)
    {
        var (archive, codec) = OpenFirmware(project);
        return Plan(project, archive, codec, progress);
    }

    public static ProjectPlan Plan(EditProject project, AllDataArchive archive, MArchiveBytes codec, IProgress<string>? progress = null)
    {
        var issues = new List<string>();
        var warnings = new List<string>();
        var patched = new List<(string Entry, PsbStreamEditor Editor)>();
        var applied = 0;

        foreach (var group in project.Edits.GroupBy(e => e.Psb))
        {
            var entry = group.Key + ".m";
            if (archive.TryGet(entry) is null)
            {
                entry = group.Key;
                if (archive.TryGet(entry) is null)
                {
                    issues.Add($"'{group.Key}' is not in the firmware archive.");
                    continue;
                }
            }

            PsbStreamEditor editor;
            IReadOnlyList<TextureSource> sources;
            try
            {
                var psb = archive.ReadDecompressed(entry);
                editor = new PsbStreamEditor(psb);
                sources = TextureCatalog.ReadPsb(psb);
            }
            catch (Exception ex)
            {
                issues.Add($"'{group.Key}' cannot be read: {ex.Message}");
                continue;
            }

            var changed = false;
            foreach (var edit in group)
            {
                var label = edit.Icon is null ? $"{edit.Psb} / {edit.Texture}" : $"{edit.Psb} / {edit.Texture} / {edit.Icon}";

                var source = sources.FirstOrDefault(s => s.Name == edit.Texture);
                if (source is null)
                {
                    issues.Add($"{label}: the file has no texture '{edit.Texture}'.");
                    continue;
                }

                IconInfo? icon = null;
                if (edit.Icon is not null)
                {
                    icon = source.Icons.FirstOrDefault(i => i.Name == edit.Icon);
                    if (icon is null)
                    {
                        issues.Add($"{label}: the texture has no icon '{edit.Icon}'.");
                        continue;
                    }
                }

                RgbaImage image;
                var imagePath = project.Resolve(edit.Image);
                try
                {
                    image = PngDecoder.Decode(File.ReadAllBytes(imagePath));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
                {
                    issues.Add($"{label}: cannot read image '{edit.Image}': {ex.Message}");
                    continue;
                }

                try
                {
                    var stream = editor.Read(source.StreamKind, source.StreamIndex).Span;
                    var replacement = TextureReplacer.Replace(source, stream, image, icon);
                    editor.Replace(source.StreamKind, source.StreamIndex, replacement);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or KeyNotFoundException)
                {
                    issues.Add($"{label}: {ex.Message}");
                    continue;
                }

                foreach (var warning in TextureEditAdvisor.Warnings(source.Format, image))
                    warnings.Add($"{label}: {warning}");

                changed = true;
                applied++;
            }

            if (changed) patched.Add((entry, editor));
        }

        var replacements = new Dictionary<string, byte[]>();
        if (issues.Count == 0)
        {
            foreach (var (entry, editor) in patched)
            {
                progress?.Report($"Compressing {entry}");
                var bytes = editor.ToArray();
                replacements[entry] = entry.EndsWith(".m", StringComparison.OrdinalIgnoreCase)
                    ? codec.Compress(Path.GetFileName(entry), bytes)
                    : bytes;
            }
        }

        var tree = new List<TreeChange>();
        if (project.Extracted is { Length: > 0 } extracted && Directory.Exists(project.Resolve(extracted)))
        {
            var scan = TreePacker.Scan(archive, codec, project.Resolve(extracted), progress);
            issues.AddRange(scan.Issues);
            warnings.AddRange(scan.Warnings);
            tree.AddRange(scan.Changes);
            foreach (var change in scan.Changes.Where(c => patched.Any(p => p.Entry == c.Entry)))
                issues.Add($"{change.Entry} has a texture edit and was also changed in the extracted folder; keep only one of them.");

            if (issues.Count == 0)
                foreach (var change in scan.Changes)
                    replacements[change.Entry] = scan.Replacements[change.Entry];
        }

        return new ProjectPlan(replacements, issues, warnings, applied, tree);
    }

    static (AllDataArchive Archive, MArchiveBytes Codec) OpenFirmware(EditProject project)
    {
        var folder = project.Resolve(project.Firmware);
        var profile = ConsoleProfile.All.FirstOrDefault(p => p.Kind == project.Console)
            ?? throw new NotSupportedException($"{project.Console} is not supported yet.");

        var seed = project.Seed;
        if (string.IsNullOrEmpty(seed))
        {
            var engine = Path.Combine(folder, "m2engage");
            seed = File.Exists(engine) ? PsbKeyFinder.FindInFile(engine) : null;
        }
        if (string.IsNullOrEmpty(seed))
            throw new InvalidOperationException("No seed: the firmware folder has no usable m2engage and the project sets none.");

        var codec = new MArchiveBytes(profile, seed);
        var archive = AllDataArchive.Open(Path.Combine(folder, "alldata.psb.m"), Path.Combine(folder, "alldata.bin"), codec);
        return (archive, codec);
    }
}

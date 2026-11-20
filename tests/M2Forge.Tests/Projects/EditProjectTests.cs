using M2Forge.Core.Firmware;
using M2Forge.Core.Projects;

namespace M2Forge.Tests.Projects;

public class EditProjectTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-prj-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_KeepsEverything()
    {
        var project = new EditProject
        {
            Console = ConsoleKind.MegaDriveMini2,
            Firmware = "firmware/032",
            Seed = "abc",
            Edits = [new TextureEdit("system/motion/a.psb", "tex#000", "0005", "edits/logo.png"), new TextureEdit("b.psb", "0", null, "x.png")],
        };
        var path = Path.Combine(work, "mod.m2forge.json");
        Directory.CreateDirectory(work);

        project.Save(path);
        var loaded = EditProject.Load(path);

        Assert.Equal(ConsoleKind.MegaDriveMini2, loaded.Console);
        Assert.Equal("firmware/032", loaded.Firmware);
        Assert.Equal("abc", loaded.Seed);
        Assert.Equal(project.Edits, loaded.Edits);
        Assert.Equal(work, loaded.BaseDirectory);
        Assert.Contains("\"MegaDriveMini2\"", File.ReadAllText(path));
        Assert.DoesNotContain("\r", File.ReadAllText(path));
        Assert.DoesNotContain("\"Icon\": null", File.ReadAllText(path));
    }

    [Fact]
    public void Resolve_IsRelativeToTheProjectFile()
    {
        var project = new EditProject { BaseDirectory = work };

        Assert.Equal(Path.GetFullPath(Path.Combine(work, "edits", "a.png")), project.Resolve("edits/a.png"));
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere", "b.png"));
        Assert.Equal(absolute, project.Resolve(absolute));
    }

    [Fact]
    public void RelativeToProject_ShortensPathsUnderTheProject()
    {
        var project = new EditProject { BaseDirectory = work };

        Assert.Equal("edits/a.png", project.RelativeToProject(Path.Combine(work, "edits", "a.png")));
    }

    [Fact]
    public void AddOrReplace_ReplacesTheSameTarget()
    {
        var project = new EditProject();
        project.AddOrReplace(new TextureEdit("a.psb", "tex", "i", "one.png"));
        project.AddOrReplace(new TextureEdit("a.psb", "tex", null, "whole.png"));
        project.AddOrReplace(new TextureEdit("a.psb", "tex", "i", "two.png"));

        Assert.Equal(["two.png", "whole.png"], project.Edits.Select(e => e.Image));
    }

    [Fact]
    public void Remove_DeletesOnlyTheMatchingEdit()
    {
        var project = new EditProject();
        project.AddOrReplace(new TextureEdit("a.psb", "tex", "i", "one.png"));
        project.AddOrReplace(new TextureEdit("a.psb", "tex", null, "whole.png"));

        Assert.True(project.Remove("a.psb", "tex", "i"));
        Assert.False(project.Remove("a.psb", "tex", "i"));
        Assert.Single(project.Edits);
    }

    [Fact]
    public void Load_RejectsOtherVersions()
    {
        Directory.CreateDirectory(work);
        var path = Path.Combine(work, "old.json");
        File.WriteAllText(path, "{\"Version\": 99}");

        Assert.Throws<NotSupportedException>(() => EditProject.Load(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

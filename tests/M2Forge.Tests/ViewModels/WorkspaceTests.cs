using M2Forge.App.ViewModels;
using M2Forge.Core.Firmware;
using M2Forge.Core.Projects;

namespace M2Forge.Tests.ViewModels;

public class WorkspaceTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-ws-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SetEdit_AddsReplacesAndMarksTheProjectDirty()
    {
        var workspace = new Workspace();
        var raised = 0;
        workspace.EditsChanged += (_, _) => raised++;

        workspace.SetEdit(new TextureEdit("a.psb", "tex", "i", "one.png"));
        workspace.SetEdit(new TextureEdit("a.psb", "tex", "i", "two.png"));
        workspace.SetEdit(new TextureEdit("a.psb", "tex", null, "whole.png"));

        Assert.True(workspace.IsDirty);
        Assert.Equal(3, raised);
        Assert.Equal(["two.png", "whole.png"], workspace.Edits.Select(e => e.Image));
        Assert.Equal("two.png", workspace.FindEdit("a.psb", "tex", "i")!.Image);
    }

    [Fact]
    public void RemoveEdit_OnlyRemovesTheMatch()
    {
        var workspace = new Workspace();
        workspace.SetEdit(new TextureEdit("a.psb", "tex", "i", "one.png"));

        Assert.False(workspace.RemoveEdit("a.psb", "tex", null));
        Assert.True(workspace.RemoveEdit("a.psb", "tex", "i"));
        Assert.Empty(workspace.Edits);
    }

    [Fact]
    public void SaveThenLoad_StoresRelativePathsAndRestoresAbsoluteOnes()
    {
        var firmware = Path.Combine(work, "fw");
        var extracted = Path.Combine(work, "extracted");
        var image = Path.Combine(work, "edits", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        Directory.CreateDirectory(firmware);

        var workspace = new Workspace { FirmwareFolder = firmware, ExtractedFolder = extracted, Profile = ConsoleProfile.MegaDriveMini2 };
        workspace.SetEdit(new TextureEdit("system/a.psb", "tex#000", "0005", image));
        var path = Path.Combine(work, "mod.m2forge.json");

        workspace.Save(path);

        Assert.False(workspace.IsDirty);
        Assert.Equal("mod", workspace.ProjectName);
        var text = File.ReadAllText(path);
        Assert.Contains("\"edits/logo.png\"", text);
        Assert.Contains("\"fw\"", text);
        Assert.DoesNotContain(work.Replace('\\', '/'), text);

        var other = new Workspace();
        other.Load(path);

        Assert.Equal(ConsoleProfile.MegaDriveMini2, other.Profile);
        Assert.Equal(Path.GetFullPath(firmware), other.FirmwareFolder);
        Assert.Equal(Path.GetFullPath(extracted), other.ExtractedFolder);
        Assert.Equal(Path.GetFullPath(image), Assert.Single(other.Edits).Image);
        Assert.False(other.IsDirty);
        Assert.Equal("mod", other.ProjectName);
    }

    [Fact]
    public void ToProject_OmitsTheSeedWhenTheFirmwareCanProvideIt()
    {
        Directory.CreateDirectory(work);
        var workspace = new Workspace { FirmwareFolder = work, Seed = "abc" };

        Assert.Equal("abc", workspace.ToProject().Seed);

        File.WriteAllBytes(Path.Combine(work, "m2engage"), [1]);
        Assert.Null(workspace.ToProject().Seed);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var workspace = new Workspace { FirmwareFolder = "x", ExtractedFolder = "y", Seed = "z" };
        workspace.SetEdit(new TextureEdit("a.psb", "tex", null, "a.png"));
        var raised = 0;
        workspace.EditsChanged += (_, _) => raised++;

        workspace.Reset();

        Assert.Empty(workspace.Edits);
        Assert.False(workspace.IsDirty);
        Assert.Null(workspace.ProjectPath);
        Assert.Equal(string.Empty, workspace.FirmwareFolder);
        Assert.Equal("Untitled project", workspace.ProjectName);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ProjectName_HandlesTheDoubleExtension()
    {
        var workspace = new Workspace { ProjectPath = Path.Combine(work, "my mod.m2forge.json") };
        Assert.Equal("my mod", workspace.ProjectName);

        workspace.ProjectPath = Path.Combine(work, "plain.json");
        Assert.Equal("plain", workspace.ProjectName);
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

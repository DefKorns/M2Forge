using M2Forge.Core.Firmware;

namespace M2Forge.Tests.Firmware;

public sealed class FirmwareExtractorTests : IDisposable
{
    readonly string output = Path.Combine(Path.GetTempPath(), "m2forge-test-" + Guid.NewGuid().ToString("N"));

    [FirmwareFact]
    public void Extract_ProducesDeserializedUi()
    {
        var folder = Environment.GetEnvironmentVariable(FirmwareFactAttribute.Variable)!;
        var seed = PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage"))!;

        var result = new FirmwareExtractor(ConsoleProfile.MegaDriveMini, seed)
            .Extract(Path.Combine(folder, "alldata.psb.m"), output);

        Assert.Empty(result.Failures);
        Assert.True(result.FilesExtracted > 0);
        Assert.Contains("\"RGBA8\"", File.ReadAllText(Path.Combine(output, "system", "motion", "titleselect_ui.psb.json")));
        Assert.True(File.Exists(Path.Combine(output, "system", "motion", "titleselect_ui.psb.streams", "stream_1")));
        Assert.Empty(Directory.GetFiles(output, "*.m", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
    }
}

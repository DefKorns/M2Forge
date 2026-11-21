using M2Forge.Core.Psb;

namespace M2Forge.Tests.Psb;

public sealed class PsbRoundTripTests
{
    static List<string> FirmwarePsbFiles()
    {
        var reference = Environment.GetEnvironmentVariable("M2FORGE_TEST_REFERENCE");
        Assert.True(!string.IsNullOrEmpty(reference) && Directory.Exists(reference), "Set M2FORGE_TEST_REFERENCE.");
        return Directory.EnumerateFiles(reference!, "*.psb", SearchOption.AllDirectories).OrderBy(f => f).ToList();
    }

    [FirmwareFact]
    public void EveryFirmwarePsb_SurvivesReserializationWithSameContent()
    {
        var files = FirmwarePsbFiles();
        Assert.Equal(119, files.Count);

        var results = files.Select(f => PsbRoundTrip.Run(f)).ToList();

        Assert.Empty(results.Where(r => !r.TreeEqual).Select(r => r.Path));
        Assert.Empty(results.Where(r => !r.StreamsEqual).Select(r => r.Path));
    }

    [FirmwareFact]
    public void FirmwarePsb_ByteIdenticalCountDoesNotRegress()
    {
        var results = FirmwarePsbFiles().Select(f => PsbRoundTrip.Run(f)).ToList();

        Assert.True(results.Count(r => r.BytesIdentical) >= 18);
        Assert.All(results.Where(r => r.Version == 4 && r.Path.Contains("font")), r => Assert.True(r.BytesIdentical));
    }

    [Fact]
    public void Run_ReportsBytesIdenticalForUnchangedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "m2forge-psb-" + Guid.NewGuid().ToString("N") + ".psb");
        try
        {
            var root = Newtonsoft.Json.Linq.JToken.Parse("{\"id\":\"x\",\"list\":[1,2,3],\"name\":\"a\"}");
            using (var file = File.Create(path))
                new GMWare.M2.Psb.PsbWriter(root, null) { Version = 4 }.Write(file);

            var result = PsbRoundTrip.Run(path);

            Assert.True(result.Equivalent);
            Assert.True(result.BytesIdentical);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using System.Security.Cryptography;
using GMWare.M2.MArchive;
using M2Forge.Core.Firmware;

namespace M2Forge.Tests.Firmware;

public sealed class ReferenceComparisonTests : IDisposable
{
    public const string ReferenceVariable = "M2FORGE_TEST_REFERENCE";

    readonly string output = Path.Combine(Path.GetTempPath(), "m2forge-ref-" + Guid.NewGuid().ToString("N"));

    static string? Reference()
    {
        var folder = Environment.GetEnvironmentVariable(ReferenceVariable);
        return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;
    }

    static Dictionary<string, string> Hashes(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(root, f).Replace('\\', '/'),
                f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [FirmwareFact]
    public void Extract_MatchesMArchiveBatchToolOutput()
    {
        var reference = Reference();
        Assert.True(reference is not null, $"Set {ReferenceVariable} to the folder produced by MArchiveBatchTool fullunpack.");

        var folder = Environment.GetEnvironmentVariable(FirmwareFactAttribute.Variable)!;
        var seed = PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage"))!;
        new FirmwareExtractor(ConsoleProfile.MegaDriveMini, seed).Extract(Path.Combine(folder, "alldata.psb.m"), output);

        var expected = Hashes(reference!);
        var actual = Hashes(output);

        Assert.Empty(expected.Keys.Except(actual.Keys));
        Assert.Empty(actual.Keys.Except(expected.Keys));
        Assert.Empty(expected.Where(kv => actual[kv.Key] != kv.Value).Select(kv => kv.Key));
    }

    [FirmwareFact]
    public void CompressThenDecompress_RoundTripsWithZstdSharp()
    {
        var folder = Environment.GetEnvironmentVariable(FirmwareFactAttribute.Variable)!;
        var seed = PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage"))!;
        var packer = new MArchivePacker(new ZstdSharpCodec(), seed, 64);

        Directory.CreateDirectory(output);
        var source = Path.Combine(output, "sample.nut");
        var original = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 31 % 251)).ToArray();
        File.WriteAllBytes(source, original);

        packer.CompressFile(source);
        Assert.False(File.Exists(source));
        packer.DecompressFile(source + ".m");

        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [FirmwareFact]
    public void Compress_IsReadableByMArchiveBatchTool()
    {
        var tool = Environment.GetEnvironmentVariable("M2FORGE_TEST_BATCH_EXE");
        Assert.True(!string.IsNullOrEmpty(tool) && File.Exists(tool), "Set M2FORGE_TEST_BATCH_EXE to MArchiveBatchTool.exe.");

        var folder = Environment.GetEnvironmentVariable(FirmwareFactAttribute.Variable)!;
        var seed = PsbKeyFinder.FindInFile(Path.Combine(folder, "m2engage"))!;
        var reference = Reference();
        Assert.True(reference is not null, $"Set {ReferenceVariable}.");

        var sample = Path.Combine(reference!, "system", "motion", "titleselect_ui.psb");
        var original = File.ReadAllBytes(sample);
        Directory.CreateDirectory(output);
        var target = Path.Combine(output, "titleselect_ui.psb");
        File.WriteAllBytes(target, original);

        new MArchivePacker(new ZstdSharpCodec(), seed, 64).CompressFile(target);
        Assert.False(File.Exists(target));

        var info = new System.Diagnostics.ProcessStartInfo(tool!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = output,
        };
        foreach (var arg in new[] { "m", "unpack", target + ".m", "zstd", seed, "64" })
            info.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(info)!)
        {
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        Assert.Equal(original, File.ReadAllBytes(target));
    }

    public void Dispose()
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
    }
}

using System.Security.Cryptography;

namespace M2Forge.Tests;

public static class TestSupport
{
    public static string FirmwareFolder() => Required(FirmwareFactAttribute.Variable);

    public static string ReferenceFolder() => Required("M2FORGE_TEST_REFERENCE");

    public static string BatchTool() => Required("M2FORGE_TEST_BATCH_EXE");

    static string Required(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        Assert.True(!string.IsNullOrEmpty(value) && (Directory.Exists(value) || File.Exists(value)), $"Set {variable}.");
        return value!;
    }

    public static Dictionary<string, string> Hashes(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(root, f).Replace('\\', '/'),
                f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    public static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static void AssertSameTree(string expectedRoot, string actualRoot)
    {
        var expected = Hashes(expectedRoot);
        var actual = Hashes(actualRoot);

        Assert.Empty(expected.Keys.Except(actual.Keys));
        Assert.Empty(actual.Keys.Except(expected.Keys));
        Assert.Empty(expected.Where(kv => actual[kv.Key] != kv.Value).Select(kv => kv.Key));
    }

    public static void RunBatchFullUnpack(string workingDirectory, string seed)
    {
        var info = new System.Diagnostics.ProcessStartInfo(BatchTool())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var arg in new[] { "fullunpack", Path.Combine(workingDirectory, "alldata.psb.m"), "zstd", seed, "64" })
            info.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}

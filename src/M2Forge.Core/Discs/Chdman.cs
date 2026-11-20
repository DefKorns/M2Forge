using System.Diagnostics;

namespace M2Forge.Core.Discs;

public static class Chdman
{
    public static string? Find(IEnumerable<string>? extraFolders = null)
    {
        var name = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";
        var folders = new List<string> { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "tools") };
        if (extraFolders is not null) folders.AddRange(extraFolders);
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        return folders
            .Select(folder => Path.Combine(folder.Trim('"'), name))
            .FirstOrDefault(File.Exists);
    }

    public static async Task CompressAsync(string chdmanPath, string cuePath, string chdPath, CancellationToken cancellation = default)
    {
        var start = new ProcessStartInfo(chdmanPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "createcd", "-i", cuePath, "-o", chdPath, "--force" }) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new IOException("chdman could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var errors = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var text = (await errors.ConfigureAwait(false)).Trim();
            if (text.Length == 0) text = (await output.ConfigureAwait(false)).Trim();
            throw new IOException($"chdman failed ({process.ExitCode}): {text}");
        }
    }
}

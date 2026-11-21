using M2Forge.Core.Discs;

namespace M2Forge.Tests.Discs;

public sealed class ChdTests
{
    [McdFact]
    public async Task ACompressedDisc_ExtractsBackToTheSameBin()
    {
        var chdman = Environment.GetEnvironmentVariable("M2FORGE_TEST_CHDMAN");
        if (string.IsNullOrEmpty(chdman) || !File.Exists(chdman)) return;

        var mcd = Environment.GetEnvironmentVariable(McdFactAttribute.Variable)!;
        var folder = Path.Combine(Path.GetTempPath(), "m2forge-chd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plain = await DiscConverter.ConvertAsync(mcd, Path.Combine(folder, "plain", "disc"));
            var chd = Path.Combine(folder, "disc.chd");
            await Chdman.CompressAsync(chdman, plain[0].CuePath, chd);
            Assert.True(new FileInfo(chd).Length < new FileInfo(plain[0].BinPath).Length);

            var back = Path.Combine(folder, "back.cue");
            var start = new System.Diagnostics.ProcessStartInfo(chdman) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "extractcd", "-i", chd, "-o", back, "-ob", Path.Combine(folder, "back.bin") }) start.ArgumentList.Add(a);
            using (var process = System.Diagnostics.Process.Start(start)!) await process.WaitForExitAsync();

            Assert.Equal(await File.ReadAllBytesAsync(plain[0].BinPath), await File.ReadAllBytesAsync(Path.Combine(folder, "back.bin")));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}

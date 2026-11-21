using M2Forge.Core.Roms;

namespace M2Forge.Tests.Roms;

public sealed class RomExporterTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "m2forge-rex-" + Guid.NewGuid().ToString("N"));

    RomInfo Rom(string title, string region, string language, byte[] bytes)
    {
        var source = Path.Combine(work, "source");
        Directory.CreateDirectory(source);
        var fileName = $"{region}_{language}_{title.Replace(' ', '_')}.bin";
        var path = Path.Combine(source, fileName);
        File.WriteAllBytes(path, bytes);
        return new RomInfo(fileName, path, region, language, title, bytes.Length, null);
    }

    [Fact]
    public void ExportName_KeepsTheFirmwareFileNameAndAppliesTheExtension()
    {
        var rom = new RomInfo("eu_en_alex_kidd.bin", "x.bin", "eu", "en", "Alex Kidd", 1, null);

        Assert.Equal("eu_en_alex_kidd.bin", RomExporter.ExportName(rom));
        Assert.Equal("eu_en_alex_kidd.gen", RomExporter.ExportName(rom, "gen"));
        Assert.Equal("eu_en_alex_kidd.md", RomExporter.ExportName(rom, ".md"));
    }

    [Fact]
    public void ExportName_IgnoresTheDisplayTitle()
    {
        Assert.Equal("Tetris.bin", RomExporter.ExportName(new RomInfo("Tetris.bin", "p", "", "", "Tetris", 1, null)));
        Assert.Equal("a.bin", RomExporter.ExportName(new RomInfo("a.bin", "p", "", "", "Fun: Game?", 1, null)));
        Assert.Equal("a.bin", RomExporter.ExportName(new RomInfo("a.bin", "p", "", "", "", 1, null)));
    }

    [Fact]
    public void Export_AsZip_PutsTheNamedRomInsideAndKeepsItsBytes()
    {
        var data = Enumerable.Range(0, 5000).Select(i => (byte)(i % 7)).ToArray();
        var rom = Rom("Zipped Game", "us", "en", data);
        var target = Path.Combine(work, "out");

        var result = RomExporter.Export([rom], target, ".bin", zip: true);

        Assert.Equal(1, result.Exported);
        var zipPath = Path.Combine(target, "us_en_Zipped_Game.zip");
        Assert.True(File.Exists(zipPath));
        Assert.Empty(Directory.GetFiles(target, "*.part"));
        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("us_en_Zipped_Game.bin", entry.Name);
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        Assert.Equal(data, memory.ToArray());
    }

    [Fact]
    public void Export_CopiesBytesExactly()
    {
        var data = new byte[5000];
        new Random(8).NextBytes(data);
        var rom = Rom("Alex Kidd", "eu", "en", data);

        var result = RomExporter.Export([rom], Path.Combine(work, "out"));

        Assert.Equal(1, result.Exported);
        Assert.Equal(5000, result.Bytes);
        Assert.Empty(result.Failures);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(work, "out", "eu_en_Alex_Kidd.bin")));
    }

    [Fact]
    public void Export_SkipsExistingFilesUnlessAskedToOverwrite()
    {
        var rom = Rom("Alex Kidd", "eu", "en", [1, 2, 3]);
        var output = Path.Combine(work, "out");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "eu_en_Alex_Kidd.bin"), [9, 9]);

        var skipped = RomExporter.Export([rom], output);
        Assert.Equal((0, 1), (skipped.Exported, skipped.Skipped));
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(output, "eu_en_Alex_Kidd.bin")));

        var overwritten = RomExporter.Export([rom], output, overwrite: true);
        Assert.Equal((1, 0), (overwritten.Exported, overwritten.Skipped));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(output, "eu_en_Alex_Kidd.bin")));
    }

    [Fact]
    public void Export_KeepsRegionVariantsApart()
    {
        var roms = new[]
        {
            Rom("Alex Kidd", "eu", "en", [1]),
            Rom("Alex Kidd", "us", "en", [2]),
            Rom("Alex Kidd", "eu", "fr", [3]),
        };

        var result = RomExporter.Export(roms, Path.Combine(work, "out"));

        Assert.Equal(3, result.Exported);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(work, "out")).Length);
    }

    [Fact]
    public void Export_ReportsAFailureAndContinues()
    {
        var good = Rom("Good", "eu", "en", [1]);
        var missing = new RomInfo("gone.bin", Path.Combine(work, "nowhere.bin"), "eu", "en", "Gone", 1, null);

        var result = RomExporter.Export([missing, good], Path.Combine(work, "out"));

        Assert.Equal(1, result.Exported);
        Assert.Single(result.Failures);
        Assert.Contains("gone.bin", result.Failures[0]);
    }

    [Fact]
    public void ComputeHashes_MatchesKnownVectors()
    {
        Directory.CreateDirectory(work);
        var path = Path.Combine(work, "check.bin");
        File.WriteAllText(path, "123456789");

        var hashes = RomExporter.ComputeHashes(path);

        Assert.Equal("cbf43926", hashes.Crc32);
        Assert.Equal("f7c3bc1d808e04732adf679965ccc34ca7ae3441", hashes.Sha1);
    }

    [FirmwareFact]
    public void Firmware_HasNinetyRomsWithHeadersAndUniqueExportNames()
    {
        var reference = TestSupport.ReferenceFolder();

        var roms = RomLibrary.Scan(reference);

        Assert.Equal(90, roms.Count);
        Assert.All(roms, r =>
        {
            Assert.NotNull(r.Header);
            Assert.NotEqual(string.Empty, r.Region);
            Assert.NotEqual(string.Empty, r.Language);
            Assert.True(r.Size >= 0x20000);
        });
        Assert.Equal(90, roms.Select(r => RomExporter.ExportName(r)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(roms.Sum(r => r.Size) > 100_000_000);

        var sample = roms.Take(3).ToList();
        var result = RomExporter.Export(sample, Path.Combine(work, "out"));
        Assert.Equal(3, result.Exported);
        foreach (var rom in sample)
            Assert.Equal(
                TestSupport.Sha256(rom.Path),
                TestSupport.Sha256(Path.Combine(work, "out", RomExporter.ExportName(rom))));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}

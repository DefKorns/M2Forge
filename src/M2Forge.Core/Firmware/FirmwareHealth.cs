namespace M2Forge.Core.Firmware;

public enum FirmwareHealth
{
    Checking,
    Verified,
    Intact,
    Modified,
    Damaged,
}

public sealed record HealthReport(FirmwareHealth Health, string Detail, string? Name = null)
{
    public bool IsUsable => Health != FirmwareHealth.Damaged;
}

public sealed record StructureReport(IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0;
}

public static class FirmwareHealthCheck
{
    public static StructureReport CheckStructure(string folder)
    {
        var problems = new List<string>();
        var manifest = FirstExisting(folder, "alldata.psb.m", "alldata.psb");
        var bin = Path.Combine(folder, "alldata.bin");
        var engine = Path.Combine(folder, "m2engage");

        if (manifest is null) problems.Add("alldata.psb.m is missing.");
        if (!File.Exists(bin)) problems.Add("alldata.bin is missing.");
        if (!File.Exists(engine)) problems.Add("m2engage is missing.");
        if (problems.Count > 0) return new StructureReport(problems);

        var key = PsbKeyFinder.FindInFile(engine);
        if (key is null)
        {
            problems.Add("The archive key could not be read from m2engage.");
            return new StructureReport(problems);
        }

        AllDataArchive archive;
        try
        {
            archive = AllDataArchive.Open(manifest!, bin, new MArchiveBytes(ConsoleProfile.MegaDriveMini, key));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            problems.Add("The archive index cannot be read: " + ex.Message);
            return new StructureReport(problems);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Zstd", StringComparison.Ordinal))
        {
            problems.Add("The archive index is damaged.");
            return new StructureReport(problems);
        }

        var length = new FileInfo(bin).Length;
        var beyond = archive.Entries.Count(e => e.Offset + e.Length > length);
        if (beyond > 0)
            problems.Add($"alldata.bin is shorter than its index says ({beyond} file(s) are cut off). The copy is probably incomplete.");
        if (archive.Entries.Count == 0)
            problems.Add("The archive index lists no files.");

        return new StructureReport(problems);
    }

    public static HealthReport Assess(string folder)
    {
        var structure = CheckStructure(folder);
        if (!structure.Ok) return new HealthReport(FirmwareHealth.Damaged, structure.Problems[0]);

        var report = FirmwareValidator.Validate(folder);
        if (report.IsGenuine) return new HealthReport(FirmwareHealth.Verified, "These files match a known, unmodified firmware.", report.Match!.Name);
        if (report.Match is not null)
        {
            var changed = string.Join(", ", report.Files.Where(f => f.Verdict != FileVerdict.Verified).Select(f => f.Name));
            return new HealthReport(FirmwareHealth.Modified, $"{changed} differ from the known firmware.", report.Match.Name);
        }

        return new HealthReport(FirmwareHealth.Intact, "The files are complete, but this version is not in the list of known firmwares.");
    }

    static string? FirstExisting(string folder, params string[] names) =>
        names.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists);
}

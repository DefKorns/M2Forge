namespace M2Forge.Core.Firmware;

public sealed record ValidationReport(KnownFirmware? Match, IReadOnlyList<FileCheck> Files)
{
    public bool IsGenuine => Match is not null && Files.All(f => f.Verdict == FileVerdict.Verified);
    public bool HasModifiedFiles => Files.Any(f => f.Verdict == FileVerdict.Modified);
    public bool IsMissingFiles => Files.Any(f => f.Verdict == FileVerdict.Missing);
}

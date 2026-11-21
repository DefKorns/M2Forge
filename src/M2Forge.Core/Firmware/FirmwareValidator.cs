using System.Security.Cryptography;

namespace M2Forge.Core.Firmware;

public static class FirmwareValidator
{
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static KnownFirmware? IdentifyArchive(string archiveSha256) =>
        FirmwareCatalog.All.FirstOrDefault(f => f.ArchiveSha256.Length > 0 && f.ArchiveSha256 == archiveSha256);

    public static ValidationReport Validate(string folder, IProgress<string>? progress = null)
    {
        var hashes = new Dictionary<string, string?>();
        foreach (var name in FirmwareImporter.Wanted)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
                hashes[name] = null;
                continue;
            }
            progress?.Report("Hashing " + name);
            hashes[name] = Sha256(path);
        }

        var match = FirmwareCatalog.All.FirstOrDefault(f => f.BinSha256 == hashes["alldata.bin"])
                    ?? FirmwareCatalog.All.FirstOrDefault(f => f.ManifestSha256 == hashes["alldata.psb.m"]);

        var files = new List<FileCheck>
        {
            Check("alldata.psb.m", hashes["alldata.psb.m"], match?.ManifestSha256),
            Check("alldata.bin", hashes["alldata.bin"], match?.BinSha256),
            Check("m2engage", hashes["m2engage"], match?.EngineSha256),
        };
        return new ValidationReport(match, files);
    }

    static FileCheck Check(string name, string? actual, string? expected)
    {
        if (actual is null) return new FileCheck(name, FileVerdict.Missing, string.Empty);
        if (expected is null) return new FileCheck(name, FileVerdict.Unknown, actual);
        return new FileCheck(name, actual == expected ? FileVerdict.Verified : FileVerdict.Modified, actual);
    }
}

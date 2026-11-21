namespace M2Forge.Core.Firmware;

public sealed record KnownFirmware(
    string Name,
    string Build,
    string ManifestSha256,
    string BinSha256,
    string EngineSha256,
    string ArchiveSha256,
    ConsoleKind Console = ConsoleKind.MegaDriveMini,
    long BinSize = 0);

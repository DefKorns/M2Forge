using System.IO.Hashing;

namespace M2Forge.Core.Roms;

public sealed record RomExportResult(int Exported, int Skipped, long Bytes, IReadOnlyList<string> Failures);

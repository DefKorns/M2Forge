using GMWare.M2.Psb;
using M2Forge.Core.Psb;

namespace M2Forge.Core.Firmware;

public sealed record ExtractionResult(int FilesExtracted, int ArchivesDecompressed, int PsbsDeserialized, IReadOnlyList<string> Failures);

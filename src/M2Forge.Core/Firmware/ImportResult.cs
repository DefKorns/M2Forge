using SharpCompress.Archives.SevenZip;

namespace M2Forge.Core.Firmware;

public sealed record ImportResult(string Folder, IReadOnlyList<string> Files, ConsoleKind Console);

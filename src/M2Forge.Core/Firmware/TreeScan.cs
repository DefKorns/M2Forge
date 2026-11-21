namespace M2Forge.Core.Firmware;

public sealed record TreeScan(
    IReadOnlyList<TreeChange> Changes,
    IReadOnlyDictionary<string, byte[]> Replacements,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Issues,
    int Missing,
    int Compared);

namespace M2Forge.Core.Psb;

public sealed record PsbStreamLocation(PsbStreamKind Kind, uint Index, int Offset, int Length);

namespace M2Forge.Core.Firmware;

public sealed record DiskPartition(int Number, long Offset, long Length, byte Type);

namespace M2Forge.Core.Firmware;

public sealed record TreeChange(string Entry, string RelativePath, long OldSize, long NewSize);

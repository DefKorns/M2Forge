namespace M2Forge.Core.Firmware;

public sealed record FileCheck(string Name, FileVerdict Verdict, string Sha256);

namespace M2Forge.Core.Firmware;

public sealed record StructureReport(IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0;
}

namespace M2Forge.Tests;

public sealed class FirmwareFactAttribute : FactAttribute
{
    public const string Variable = "M2FORGE_TEST_FW";

    public FirmwareFactAttribute()
    {
        var folder = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            Skip = $"Set {Variable} to a folder with alldata.psb.m, alldata.bin and m2engage.";
    }
}

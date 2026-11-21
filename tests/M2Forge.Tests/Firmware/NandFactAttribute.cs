namespace M2Forge.Tests.Firmware;

public sealed class NandFactAttribute : FactAttribute
{
    public const string Variable = "M2FORGE_TEST_NAND";

    public NandFactAttribute()
    {
        var folder = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder) || Directory.GetFiles(folder, "*.7z").Length == 0)
            Skip = $"Set {Variable} to a folder with the firmware .7z archives.";
    }
}

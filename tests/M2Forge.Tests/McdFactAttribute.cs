namespace M2Forge.Tests;

public sealed class McdFactAttribute : FactAttribute
{
    public const string Variable = "M2FORGE_TEST_MCD";

    public McdFactAttribute()
    {
        var file = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
            Skip = $"Set {Variable} to a Mega Drive Mini 2 .mcd disc image.";
    }
}

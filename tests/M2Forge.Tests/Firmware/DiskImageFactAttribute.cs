using M2Forge.Core.Firmware;
using M2Forge.Core.Roms;

namespace M2Forge.Tests.Firmware;

public sealed class DiskImageFactAttribute : FactAttribute
{
    public const string Variable = "M2FORGE_TEST_MD2_IMAGE";

    public DiskImageFactAttribute()
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            Skip = $"Set {Variable} to a Mega Drive Mini 2 eMMC image.";
    }
}

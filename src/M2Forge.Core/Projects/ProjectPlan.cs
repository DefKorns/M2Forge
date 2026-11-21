using M2Forge.Core.Firmware;
using M2Forge.Core.Textures;

namespace M2Forge.Core.Projects;

public sealed record ProjectPlan(
    IReadOnlyDictionary<string, byte[]> Replacements,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Warnings,
    int EditsApplied,
    IReadOnlyList<TreeChange>? TreeChanges = null)
{
    public IReadOnlyList<TreeChange> Tree { get; } = TreeChanges ?? [];

    public bool CanBuild => Issues.Count == 0;
}

using M2Forge.Core.Firmware;
using M2Forge.Core.Textures;

namespace M2Forge.Core.Projects;

public sealed record ProjectBuildResult(ProjectPlan Plan, AllDataBuildResult? Archive);

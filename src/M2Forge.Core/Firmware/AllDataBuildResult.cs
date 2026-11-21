using Newtonsoft.Json.Linq;

namespace M2Forge.Core.Firmware;

public sealed record AllDataBuildResult(int Replaced, long BinLength, bool ManifestRewritten);

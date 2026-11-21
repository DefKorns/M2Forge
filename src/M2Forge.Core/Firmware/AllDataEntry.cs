using Newtonsoft.Json.Linq;

namespace M2Forge.Core.Firmware;

public sealed record AllDataEntry(string Path, long Offset, long Length);

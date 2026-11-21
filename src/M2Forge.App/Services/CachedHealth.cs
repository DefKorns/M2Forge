using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using M2Forge.Core.Firmware;

namespace M2Forge.App.Services;

public sealed record CachedHealth(long BinSize, long BinTicks, FirmwareHealth Health, string Detail, string? Name);

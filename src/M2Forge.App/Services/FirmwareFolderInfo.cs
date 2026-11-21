using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using M2Forge.Core.Firmware;

namespace M2Forge.App.Services;

public sealed record FirmwareFolderInfo(
    string Name,
    string Folder,
    string ExtractedFolder,
    bool IsExtracted,
    long BinSize,
    long BinTicks,
    FirmwareMeta? Meta,
    KnownFirmware? Known)
{
    public string Title => Known?.Name ?? Name;

    public ConsoleKind? Console => Meta?.Console ?? Known?.Console;
}

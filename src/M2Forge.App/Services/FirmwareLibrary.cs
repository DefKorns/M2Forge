using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using M2Forge.Core.Firmware;

namespace M2Forge.App.Services;

public static class FirmwareLibrary
{
    const string MetaFile = "firmware.json";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<FirmwareFolderInfo> Scan()
    {
        var root = Path.Combine(AppFolders.Root, "firmware");
        if (!Directory.Exists(root)) return [];

        var result = new List<FirmwareFolderInfo>();
        foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!FirmwareImporter.Wanted.Any(n => File.Exists(Path.Combine(folder, n)))) continue;

            var bin = new FileInfo(Path.Combine(folder, "alldata.bin"));
            var size = bin.Exists ? bin.Length : 0;
            var name = Path.GetFileName(folder);
            var extracted = Path.Combine(AppFolders.Root, "extracted", name);
            result.Add(new FirmwareFolderInfo(
                name,
                folder,
                extracted,
                Directory.Exists(Path.Combine(extracted, "system")),
                size,
                bin.Exists ? bin.LastWriteTimeUtc.Ticks : 0,
                ReadMeta(folder),
                FirmwareCatalog.FindBySize(size)));
        }
        return result;
    }

    public static FirmwareMeta? ReadMeta(string folder)
    {
        var path = Path.Combine(folder, MetaFile);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<FirmwareMeta>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public static void WriteMeta(string folder, FirmwareMeta meta)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, MetaFile);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(meta, Options).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    public static HealthReport Assess(FirmwareFolderInfo info)
    {
        if (info.Meta?.Health is { } cached && cached.BinSize == info.BinSize && cached.BinTicks == info.BinTicks && info.BinSize > 0)
            return new HealthReport(cached.Health, cached.Detail, cached.Name);

        var report = FirmwareHealthCheck.Assess(info.Folder);
        try
        {
            var meta = info.Meta ?? new FirmwareMeta(string.Empty, info.Console, null);
            WriteMeta(info.Folder, meta with
            {
                Console = meta.Console ?? info.Console,
                Health = new CachedHealth(info.BinSize, info.BinTicks, report.Health, report.Detail, report.Name),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return report;
    }

    public static void Remove(FirmwareFolderInfo info)
    {
        foreach (var folder in new[] { info.Folder, info.ExtractedFolder })
        {
            if (!IsInsideAppFolder(folder) || !Directory.Exists(folder)) continue;
            Directory.Delete(folder, recursive: true);
        }
    }

    static bool IsInsideAppFolder(string folder)
    {
        var root = Path.GetFullPath(AppFolders.Root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace M2Forge.Core.Roms;

public sealed record RomInfo(
    string FileName,
    string Path,
    string Region,
    string Language,
    string Title,
    long Size,
    RomHeader? Header)
{
    public string Format => System.IO.Path.GetExtension(FileName).TrimStart('.').ToLowerInvariant();

    public bool IsMegaDriveRom => Format == RomLibrary.MegaDriveFormat;

    public string HeaderName => Header is null
        ? string.Empty
        : Header.OverseasName.Length > 0 ? Header.OverseasName : Header.DomesticName;
}

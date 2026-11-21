using System.Text;
using System.Text.RegularExpressions;

namespace M2Forge.Core.Roms;

public sealed record RomHeader(
    string Console,
    string Copyright,
    string DomesticName,
    string OverseasName,
    string Serial,
    ushort Checksum,
    string Regions);

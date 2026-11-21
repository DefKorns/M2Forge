using System.Text.RegularExpressions;

namespace M2Forge.Core.Roms;

public sealed record MenuEntry(
    string Tag,
    string MenuRegion,
    string MenuLanguage,
    string Country,
    string Title,
    int Image);

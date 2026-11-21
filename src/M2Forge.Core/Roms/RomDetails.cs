using System.Text.RegularExpressions;

namespace M2Forge.Core.Roms;

public sealed record RomDetails(string Title, string? NativeTitle, IReadOnlyList<MenuEntry> Entries)
{
    public MenuEntry? CoverEntry =>
        Entries.FirstOrDefault(e => e.MenuLanguage == "EN" && e.Image != RomCatalog.NoImage) ??
        Entries.FirstOrDefault(e => e.Image != RomCatalog.NoImage);
}

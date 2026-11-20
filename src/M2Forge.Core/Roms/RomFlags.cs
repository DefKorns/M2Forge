namespace M2Forge.Core.Roms;

public static class RomFlags
{
    public static string ForRegion(string region) =>
        region.ToLowerInvariant() switch
        {
            "eu" => "eu",
            "us" => "us",
            "jp" => "jp",
            _ => string.Empty,
        };

    public static string ForLanguage(string language) =>
        language.ToLowerInvariant() switch
        {
            "us" => "us",
            "jp" => "jp",
            "eu" => "eu",
            "en" => "gb",
            "de" => "de",
            "fr" => "fr",
            "es" or "sp" => "es",
            "ko" => "kr",
            "ch" => "cn",
            _ => string.Empty,
        };
}

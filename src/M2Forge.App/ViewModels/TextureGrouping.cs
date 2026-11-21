using System;
using System.Collections.Generic;
using System.Linq;

namespace M2Forge.App.ViewModels;

public static class TextureGrouping
{
    static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["titleselect_ui"] = "Menu background and icons",
        ["bg00"] = "Background 0",
        ["bg01"] = "Background 1",
        ["wall_bg00"] = "Wallpaper 0",
        ["wall_bg01_00"] = "Wallpaper 1 (part 1)",
        ["wall_bg01_01"] = "Wallpaper 1 (part 2)",
        ["emu_screen"] = "Game screen frame",
        ["logo_publisher"] = "Publisher logo",
        ["logo_m2"] = "M2 logo",
        ["warning_copy"] = "Copyright warning",
        ["itemindicator"] = "Item indicator",
        ["leaderboard"] = "Leaderboard",
        ["menu_dialog"] = "Menu dialogs",
        ["mode_title"] = "Mode title",
        ["nowloading"] = "Loading screen",
        ["replay_play"] = "Replay controls",
    };

    static readonly Dictionary<string, string> Regions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eu"] = "Europe",
        ["us"] = "USA",
        ["jp"] = "Japan",
    };

    static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eng"] = "English",
        ["kr"] = "Korean",
        ["tc"] = "Traditional Chinese",
        ["sc"] = "Simplified Chinese",
    };

    public static string Leaf(string fileName)
    {
        var slash = fileName.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? fileName[(slash + 1)..] : fileName;
    }

    public static TextureCategory Classify(string fileName)
    {
        var leaf = Leaf(fileName);
        if (IsCoverSet(leaf)) return TextureCategory.Covers;
        if (fileName.Contains("/font/", StringComparison.OrdinalIgnoreCase) || leaf.Contains("basefont", StringComparison.OrdinalIgnoreCase))
            return TextureCategory.Fonts;
        return TextureCategory.Interface;
    }

    public static string Title(string fileName)
    {
        var leaf = Leaf(fileName);
        if (KnownNames.TryGetValue(leaf, out var known)) return known;

        if (IsCoverSet(leaf))
        {
            var region = leaf[(leaf.LastIndexOf('_') + 1)..];
            return Regions.TryGetValue(region, out var name) ? $"Game covers ({name})" : "Game covers";
        }

        if (leaf.Contains("basefont", StringComparison.OrdinalIgnoreCase)) return FontTitle(leaf);

        return Prettify(leaf);
    }

    static bool IsCoverSet(string leaf) =>
        leaf.Contains("titleselect", StringComparison.OrdinalIgnoreCase) && !leaf.EndsWith("_ui", StringComparison.OrdinalIgnoreCase);

    static string FontTitle(string leaf)
    {
        var tail = leaf[(leaf.IndexOf("basefont", StringComparison.OrdinalIgnoreCase) + "basefont".Length)..].Trim('_');
        if (tail.Length == 0) return "Font (default)";

        var parts = tail.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var size = string.Empty;
        var language = string.Empty;
        foreach (var part in parts)
        {
            var digits = new string(part.TakeWhile(char.IsAsciiDigit).ToArray());
            var letters = part[digits.Length..];
            if (digits.Length > 0) size = digits;
            if (letters.Length > 0) language = Languages.TryGetValue(letters, out var known) ? known : letters;
        }

        var title = "Font" + (size.Length > 0 ? " " + size : string.Empty);
        return language.Length > 0 ? $"{title} ({language})" : title;
    }

    static string Prettify(string leaf)
    {
        var text = leaf.Replace('_', ' ').Trim();
        return text.Length == 0 ? leaf : char.ToUpperInvariant(text[0]) + text[1..];
    }
}

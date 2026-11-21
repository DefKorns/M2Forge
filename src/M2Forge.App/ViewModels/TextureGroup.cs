using System;
using System.Collections.Generic;
using System.Linq;
using M2Forge.Core.Textures;

namespace M2Forge.App.ViewModels;

public sealed class TextureGroup
{
    public TextureGroup(TextureNode file)
    {
        File = file;
        Category = TextureGrouping.Classify(file.Name);
        Title = TextureGrouping.Title(file.Name);
        ImageCount = file.Children.Sum(t => 1 + t.Children.Count);
    }

    public TextureNode File { get; }
    public TextureCategory Category { get; }
    public string Title { get; }
    public int ImageCount { get; }

    public string Path => File.Name;
    public string Subtitle => ImageCount == 1 ? "1 image" : $"{ImageCount} images";
    public string AccessibleName => $"{Title}, {Subtitle}";

    public bool Matches(string term) =>
        TitleMatches(term) || File.Children.Any(t => t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                                     t.Children.Any(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));

    bool TitleMatches(string term) =>
        Title.Contains(term, StringComparison.OrdinalIgnoreCase) || Path.Contains(term, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<TextureTile> CreateTiles(string term)
    {
        var showAll = term.Length == 0 || TitleMatches(term);
        var tiles = new List<TextureTile>();
        foreach (var texture in File.Children)
        {
            var textureMatches = texture.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            if (showAll || textureMatches) tiles.Add(new TextureTile(texture));
            foreach (var icon in texture.Children)
                if (showAll || icon.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                    tiles.Add(new TextureTile(icon));
        }
        return tiles;
    }
}

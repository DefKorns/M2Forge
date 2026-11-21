using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using M2Forge.Core.Textures;

namespace M2Forge.App.ViewModels;

public sealed partial class TextureNode(
    TextureNodeKind kind,
    string name,
    string psb,
    string detail = "",
    TextureSource? source = null,
    IconInfo? icon = null,
    IReadOnlyList<TextureNode>? children = null) : ObservableObject
{
    [ObservableProperty] bool isEdited;

    public TextureNodeKind Kind { get; } = kind;
    public string Name { get; } = name;
    public string Psb { get; } = psb;
    public string Detail { get; } = detail;
    public TextureSource? Source { get; } = source;
    public IconInfo? Icon { get; } = icon;
    public IReadOnlyList<TextureNode> Children { get; } = children ?? [];

    public MaterialIconKind IconKind => Kind switch
    {
        TextureNodeKind.File => MaterialIconKind.FileOutline,
        TextureNodeKind.Texture => MaterialIconKind.ImageOutline,
        _ => MaterialIconKind.CropFree,
    };
}

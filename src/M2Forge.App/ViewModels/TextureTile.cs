using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using M2Forge.Core.Textures;

namespace M2Forge.App.ViewModels;

public sealed partial class TextureTile : ObservableObject
{
    public TextureTile(TextureNode node)
    {
        Node = node;
    }

    public TextureNode Node { get; }

    [ObservableProperty] RgbaImage? thumbnail;

    public bool IsWhole => Node.Kind == TextureNodeKind.Texture;
    public string Label => IsWhole ? "Image " + Node.Name : Node.Name;
    public string Detail => Node.Detail;
    public string AccessibleName => IsWhole ? $"Whole image {Node.Name}, {Node.Detail}" : $"Image {Node.Name}, {Node.Detail}";
}

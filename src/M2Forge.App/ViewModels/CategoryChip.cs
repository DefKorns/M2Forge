using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace M2Forge.App.ViewModels;

public sealed partial class CategoryChip(TextureCategory category, string label) : ObservableObject
{
    public TextureCategory Category { get; } = category;
    public string Label { get; } = label;

    [ObservableProperty] bool isActive;
}

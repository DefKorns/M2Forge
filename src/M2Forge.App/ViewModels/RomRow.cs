using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using M2Forge.App.Services;
using M2Forge.Core.Roms;
using M2Forge.Core.Textures;

namespace M2Forge.App.ViewModels;

public sealed partial class RomRow : ObservableObject
{
    readonly Action selectionChanged;
    readonly RomDetails? details;
    readonly RomArtwork? artwork;
    bool coverRequested;
    RgbaImage? cover;

    public RomRow(RomInfo info, RomDetails? details, RomArtwork? artwork, Action selectionChanged)
    {
        Info = info;
        this.details = details;
        this.artwork = artwork;
        this.selectionChanged = selectionChanged;
    }

    [ObservableProperty] bool isSelected;

    public RomInfo Info { get; }
    public string Title => Info.Title;
    public string? NativeTitle => details?.NativeTitle;
    public string Subtitle => NativeTitle ?? Info.FileName;
    public string RegionFlag => RomFlags.ForRegion(Info.Region);
    public string LanguageFlag => RomFlags.ForLanguage(Info.Language);
    public bool HasLanguageFlag => LanguageFlag.Length > 0;
    public bool IsBios => Info.FileName.Contains("bios", StringComparison.OrdinalIgnoreCase);

    public RgbaImage? Cover
    {
        get
        {
            if (!coverRequested)
            {
                coverRequested = true;
                _ = LoadCoverAsync(CoverKind.Cover, image => cover = image, nameof(Cover));
            }
            return cover;
        }
    }

    public async Task<RgbaImage?> LoadAsync(CoverKind kind)
    {
        if (artwork is null || details?.CoverEntry is not { } entry) return null;

        var set = artwork.SetForRegion(Info.Region);
        return await Task.Run(() => artwork.Get(entry, kind, set));
    }

    async Task LoadCoverAsync(CoverKind kind, Action<RgbaImage?> assign, string property)
    {
        assign(await LoadAsync(kind));
        OnPropertyChanged(property);
    }
    public string Region => Info.Region.ToUpperInvariant();
    public string Language => Info.Language.ToUpperInvariant();
    public string Format => Info.Format.ToUpperInvariant();
    public string SizeText => RomsViewModel.FormatSize(Info.Size);

    partial void OnIsSelectedChanged(bool value) => selectionChanged();
}

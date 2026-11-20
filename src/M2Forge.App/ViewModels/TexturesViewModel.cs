using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M2Forge.App.Services;
using M2Forge.Core.Projects;
using M2Forge.Core.Textures;

namespace M2Forge.App.ViewModels;

public partial class TexturesViewModel : ViewModelBase
{
    readonly Workspace workspace;
    readonly IDialogService dialogs;
    List<TextureNode> allNodes = [];
    List<TextureGroup> allGroups = [];
    int thumbVersion;
    TextureSource? atlasSource;
    RgbaImage? atlas;
    RgbaImage? shownImage;
    int version;

    public TexturesViewModel(Workspace workspace, IDialogService dialogs)
    {
        this.workspace = workspace;
        this.dialogs = dialogs;
        Categories =
        [
            new CategoryChip(TextureCategory.All, "All"),
            new CategoryChip(TextureCategory.Covers, "Game covers"),
            new CategoryChip(TextureCategory.Interface, "Menu and interface"),
            new CategoryChip(TextureCategory.Fonts, "Fonts"),
        ];
        Categories[0].IsActive = true;
        workspace.EditsChanged += (_, _) =>
        {
            RefreshEditFlags();
            _ = ShowAsync(SelectedNode);
        };
    }

    public ObservableCollection<TextureGroup> Groups { get; } = [];
    public ObservableCollection<TextureTile> Tiles { get; } = [];
    public IReadOnlyList<CategoryChip> Categories { get; }

    [ObservableProperty] string search = string.Empty;
    [ObservableProperty] TextureNode? selectedNode;
    [ObservableProperty] TextureGroup? selectedGroup;
    [ObservableProperty] TextureTile? selectedTile;
    [ObservableProperty] TextureCategory category = TextureCategory.All;
    [ObservableProperty] Bitmap? preview;
    [ObservableProperty] string summary = string.Empty;
    [ObservableProperty] bool hasTextures;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool fit = true;
    [ObservableProperty] bool showEdited = true;
    [ObservableProperty] string selectionTitle = "Nothing selected";
    [ObservableProperty] string selectionPath = string.Empty;
    [ObservableProperty] string formatText = "-";
    [ObservableProperty] string sizeText = "-";
    [ObservableProperty] string positionText = "-";
    [ObservableProperty] string requirementText = string.Empty;
    [ObservableProperty] string message = string.Empty;
    [ObservableProperty] bool messageIsError;
    [ObservableProperty] bool hasAnyEdit;
    [ObservableProperty] bool hasOwnEdit;
    [ObservableProperty] bool canEditSelection;

    public bool ShowScrolling => !Fit;
    public bool HasMessage => Message.Length > 0;
    public bool ShowOriginal
    {
        get => !ShowEdited;
        set => ShowEdited = !value;
    }

    partial void OnFitChanged(bool value) => OnPropertyChanged(nameof(ShowScrolling));
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnShowEditedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOriginal));
        _ = ShowAsync(SelectedNode, keepMessage: true);
    }

    partial void OnSearchChanged(string value) => ApplyFilter();

    partial void OnCategoryChanged(TextureCategory value)
    {
        foreach (var chip in Categories) chip.IsActive = chip.Category == value;
        ApplyFilter();
    }

    partial void OnSelectedGroupChanged(TextureGroup? value) => BuildTiles(value);

    partial void OnSelectedTileChanged(TextureTile? value) => SelectedNode = value?.Node;

    public bool IsFilteredEmpty => HasTextures && Groups.Count == 0;
    public bool HasGroup => SelectedGroup is not null;

    [RelayCommand]
    void SelectCategory(CategoryChip chip) => Category = chip.Category;

    partial void OnSelectedNodeChanged(TextureNode? value)
    {
        Message = string.Empty;
        ShowEdited = true;
        _ = ShowAsync(value);
    }

    public void Clear()
    {
        allNodes = [];
        allGroups = [];
        SelectedGroup = null;
        SelectedTile = null;
        SelectedNode = null;
        Groups.Clear();
        Tiles.Clear();
        HasTextures = false;
        Summary = string.Empty;
        Search = string.Empty;
    }

    public async Task LoadAsync(string folder)
    {
        IsBusy = true;
        try
        {
            var scan = await Task.Run(() => TextureLibrary.Scan(folder));
            allNodes = scan.Files.Select(BuildFileNode).ToList();
            allGroups = allNodes.Select(n => new TextureGroup(n)).OrderBy(g => g.Category).ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
            HasTextures = allNodes.Count > 0;
            var images = allGroups.Sum(g => g.ImageCount);
            Summary = scan.Failures.Count == 0
                ? $"{images} images in {allGroups.Count} sets"
                : $"{images} images in {allGroups.Count} sets, {scan.Failures.Count} unreadable";
            SelectedNode = null;
            Category = TextureCategory.All;
            ApplyFilter();
            RefreshEditFlags();
        }
        finally
        {
            IsBusy = false;
        }
    }

    static TextureNode BuildFileNode(TextureFile file)
    {
        var psb = file.Name + ".psb";
        var textures = file.Sources.Select(source =>
            new TextureNode(
                TextureNodeKind.Texture,
                source.Name,
                psb,
                $"{source.Format} {source.Width}x{source.Height}",
                source,
                children: source.Icons
                    .Select(icon => new TextureNode(TextureNodeKind.Icon, icon.Name, psb, $"{icon.Width}x{icon.Height}", source, icon))
                    .ToList()));
        return new TextureNode(TextureNodeKind.File, file.Name, psb, children: textures.ToList());
    }

    void ApplyFilter()
    {
        var term = Search.Trim();
        IEnumerable<TextureGroup> visible = allGroups;
        if (Category != TextureCategory.All) visible = visible.Where(g => g.Category == Category);
        if (term.Length > 0) visible = visible.Where(g => g.Matches(term));
        var list = visible.ToList();

        var keep = SelectedGroup;
        Groups.Clear();
        foreach (var group in list) Groups.Add(group);
        OnPropertyChanged(nameof(IsFilteredEmpty));

        if (keep is not null && list.Contains(keep)) BuildTiles(keep);
        else SelectedGroup = list.FirstOrDefault();
    }

    void BuildTiles(TextureGroup? group)
    {
        var token = ++thumbVersion;
        Tiles.Clear();
        SelectedTile = null;
        OnPropertyChanged(nameof(HasGroup));
        if (group is null) return;

        var tiles = group.CreateTiles(Search.Trim());
        foreach (var tile in tiles) Tiles.Add(tile);
        _ = LoadThumbnailsAsync(tiles, token);
        SelectedTile = Tiles.FirstOrDefault();
    }

    async Task LoadThumbnailsAsync(IReadOnlyList<TextureTile> tiles, int token)
    {
        var atlases = new Dictionary<TextureSource, RgbaImage?>();
        foreach (var batch in tiles.Chunk(6))
        {
            var made = await Task.Run(() => batch.Select(tile => (tile, image: MakeThumbnail(tile, atlases))).ToList());
            if (token != thumbVersion) return;

            foreach (var (tile, image) in made) tile.Thumbnail = image;
        }
    }

    static RgbaImage? MakeThumbnail(TextureTile tile, Dictionary<TextureSource, RgbaImage?> atlases)
    {
        if (tile.Node.Source is not { } source) return null;

        if (!atlases.TryGetValue(source, out var atlas))
        {
            try
            {
                atlas = source.Load();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
            {
                atlas = null;
            }
            atlases[source] = atlas;
        }
        if (atlas is null) return null;

        var icon = tile.Node.Icon;
        var image = icon is null ? atlas : atlas.Crop(icon.Left, icon.Top, icon.Width, icon.Height);
        return image.Thumbnail(144);
    }

    void RefreshEditFlags()
    {
        foreach (var file in allNodes)
        {
            var fileEdited = false;
            foreach (var texture in file.Children)
            {
                var textureEdited = false;
                foreach (var icon in texture.Children)
                {
                    icon.IsEdited = workspace.FindEdit(icon.Psb, texture.Name, icon.Name) is not null;
                    textureEdited |= icon.IsEdited;
                }
                textureEdited |= workspace.FindEdit(texture.Psb, texture.Name, null) is not null;
                texture.IsEdited = textureEdited;
                fileEdited |= textureEdited;
            }
            file.IsEdited = fileEdited;
        }
    }

    async Task ShowAsync(TextureNode? node, bool keepMessage = false)
    {
        var current = ++version;
        if (!keepMessage) Message = string.Empty;

        if (node is null || node.Source is null)
        {
            shownImage = null;
            Preview = null;
            HasAnyEdit = false;
            HasOwnEdit = false;
            CanEditSelection = false;
            SelectionTitle = node?.Name ?? "Nothing selected";
            SelectionPath = node is null ? string.Empty : $"{node.Children.Count} textures";
            FormatText = SizeText = PositionText = "-";
            RequirementText = string.Empty;
            ReplaceCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
            SavePngCommand.NotifyCanExecuteChanged();
            return;
        }

        var source = node.Source;
        var icon = node.Icon;
        var edits = workspace.Edits.Where(e => e.Psb == node.Psb && e.Texture == source.Name).ToList();
        HasAnyEdit = icon is null ? edits.Count > 0 : edits.Any(e => e.Icon == icon.Name || e.Icon is null);
        HasOwnEdit = workspace.FindEdit(node.Psb, source.Name, icon?.Name) is not null;
        CanEditSelection = true;

        SelectionTitle = icon is null ? $"Image {source.Name}" : $"Image {icon.Name}";
        SelectionPath = node.Psb;
        FormatText = source.Format.ToString();
        SizeText = icon is null ? $"{source.Width} x {source.Height}" : $"{icon.Width} x {icon.Height}";
        PositionText = icon is null ? "-" : $"{icon.Left}, {icon.Top}";
        RequirementText = icon is null
            ? $"A replacement must be exactly {source.Width} x {source.Height} pixels."
            : $"A replacement must be exactly {icon.Width} x {icon.Height} pixels.";
        ReplaceCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        SavePngCommand.NotifyCanExecuteChanged();

        try
        {
            RgbaImage image;
            if (ReferenceEquals(atlasSource, source) && atlas is not null)
            {
                image = atlas;
            }
            else
            {
                image = await Task.Run(source.Load);
                if (current != version) return;
                atlasSource = source;
                atlas = image;
            }

            var showEdits = ShowEdited && HasAnyEdit;
            var composed = showEdits ? await Task.Run(() => ApplyEdits(image, source, node.Psb, edits)) : image;
            if (current != version) return;

            shownImage = icon is null ? composed : composed.Crop(icon.Left, icon.Top, icon.Width, icon.Height);
            Preview = ToBitmap(shownImage);
        }
        catch (Exception ex)
        {
            if (current != version) return;
            shownImage = null;
            Preview = null;
            Message = "Cannot show this texture: " + ex.Message;
            MessageIsError = true;
        }
    }

    RgbaImage ApplyEdits(RgbaImage baseImage, TextureSource source, string psb, List<TextureEdit> edits)
    {
        var result = baseImage;
        foreach (var edit in edits)
        {
            try
            {
                var png = PngDecoder.Decode(File.ReadAllBytes(edit.Image));
                var icon = edit.Icon is null ? null : source.Icons.FirstOrDefault(i => i.Name == edit.Icon);
                result = result.Paste(TextureEncoder.RoundTrip(png, source.Format), icon?.Left ?? 0, icon?.Top ?? 0);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
            {
            }
        }
        return result;
    }

    static WriteableBitmap ToBitmap(RgbaImage image)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var frame = bitmap.Lock();
        var stride = image.Width * 4;
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(image.Pixels, y * stride, frame.Address + y * frame.RowBytes, stride);
        return bitmap;
    }

    [RelayCommand]
    async Task OpenFolderAsync()
    {
        if (await dialogs.PickFolderAsync("Extracted firmware folder") is not { } folder) return;
        workspace.ExtractedFolder = folder;
        workspace.RequestReload();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    async Task ReplaceAsync()
    {
        var node = SelectedNode;
        if (node?.Source is not { } source) return;

        var path = await dialogs.OpenFileAsync("Choose a PNG image", "PNG images", "*.png");
        if (path is null) return;

        RgbaImage image;
        try
        {
            image = PngDecoder.Decode(await File.ReadAllBytesAsync(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            Fail($"Cannot use that image: {ex.Message}");
            return;
        }

        var icon = node.Icon;
        var width = icon?.Width ?? source.Width;
        var height = icon?.Height ?? source.Height;
        var target = icon is null ? $"texture '{source.Name}'" : $"icon '{icon.Name}'";
        if (image.Width != width || image.Height != height)
        {
            Fail($"That image is {image.Width} x {image.Height}, but {target} is {width} x {height}.");
            return;
        }

        workspace.SetEdit(new TextureEdit(node.Psb, source.Name, icon?.Name, path));

        var warnings = TextureEditAdvisor.Warnings(source.Format, image);
        Message = warnings.Count == 0 ? $"Replaced {target}." : string.Join(" ", warnings);
        MessageIsError = false;
        ShowEdited = true;
    }

    [RelayCommand(CanExecute = nameof(HasOwnEdit))]
    void Revert()
    {
        if (SelectedNode?.Source is not { } source) return;

        workspace.RemoveEdit(SelectedNode.Psb, source.Name, SelectedNode.Icon?.Name);
        Message = "Edit removed.";
        MessageIsError = false;
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    async Task SavePngAsync()
    {
        if (shownImage is null) return;

        var name = SelectedNode?.Icon is { } picked && SelectedNode.Source is { } picked2 ? $"{picked2.Name}_{picked.Name}" : SelectedNode?.Source?.Name ?? "image";
        var path = await dialogs.SaveFileAsync("Save PNG", string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)), "png");
        if (path is null) return;

        var image = shownImage;
        await Task.Run(() => PngEncoder.Save(image, path));
        Message = "Saved " + Path.GetFileName(path);
        MessageIsError = false;
    }

    [RelayCommand(CanExecute = nameof(HasTextures))]
    async Task ExportAllAsync()
    {
        if (workspace.ExtractedFolder.Length == 0) return;
        if (await dialogs.PickFolderAsync("Export all textures to") is not { } target) return;

        var root = workspace.ExtractedFolder;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => TextureExporter.ExportTree(root, target));
            Message = $"Exported {result.Textures} textures and {result.Icons} icons.";
            MessageIsError = result.Failures.Count > 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool ShowEmpty => !HasTextures && !IsBusy;
    public bool ShowLoading => !HasTextures && IsBusy;

    partial void OnHasTexturesChanged(bool value)
    {
        ExportAllCommand.NotifyCanExecuteChanged();
        NotifyLoadState();
    }

    partial void OnIsBusyChanged(bool value) => NotifyLoadState();

    void NotifyLoadState()
    {
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowLoading));
    }

    void Fail(string text)
    {
        Message = text;
        MessageIsError = true;
    }
}

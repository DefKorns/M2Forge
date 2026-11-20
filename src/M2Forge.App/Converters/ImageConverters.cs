using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using M2Forge.App.Services;
using M2Forge.Core.Textures;

namespace M2Forge.App.Converters;

public static class Converters
{
    public static readonly IValueConverter Image = new RgbaImageConverter();
    public static readonly IValueConverter Flag = new FlagConverter();
    public static readonly IValueConverter Path = new PathDisplayConverter();
}

sealed class RgbaImageConverter : IValueConverter
{
    readonly ConditionalWeakTable<RgbaImage, Bitmap> cache = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RgbaImage image) return null;
        if (cache.TryGetValue(image, out var existing)) return existing;

        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var frame = bitmap.Lock())
        {
            var rowBytes = image.Width * 4;
            for (var y = 0; y < image.Height; y++)
                Marshal.Copy(image.Pixels, y * rowBytes, frame.Address + y * frame.RowBytes, rowBytes);
        }
        cache.Add(image, bitmap);
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

sealed class PathDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? AppFolders.Display(text) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

sealed class FlagConverter : IValueConverter
{
    readonly Dictionary<string, Bitmap?> cache = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } code) return null;
        if (cache.TryGetValue(code, out var cached)) return cached;

        Bitmap? bitmap = null;
        var uri = new Uri($"avares://M2Forge.App/Assets/flags/{code}.png");
        if (AssetLoader.Exists(uri))
        {
            using var stream = AssetLoader.Open(uri);
            bitmap = new Bitmap(stream);
        }
        cache[code] = bitmap;
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

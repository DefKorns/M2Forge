using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using M2Forge.App.Services;
using M2Forge.Core.Firmware;

namespace M2Forge.App.ViewModels;

public sealed partial class FirmwareEntry : ObservableObject
{
    public FirmwareEntry(FirmwareFolderInfo info)
    {
        Info = info;
    }

    public FirmwareFolderInfo Info { get; }

    [ObservableProperty] FirmwareHealth health = FirmwareHealth.Checking;
    [ObservableProperty] string healthDetail = "Checking that the files are complete...";
    [ObservableProperty] bool isCurrent;

    public string Title => Info.Title;

    public string Subtitle
    {
        get
        {
            var console = ConsoleProfile.All.FirstOrDefault(p => p.Kind == Info.Console)?.Name;
            var parts = new[] { console, FormatSize(Info.BinSize), Info.IsExtracted ? "Extracted" : null };
            return string.Join("  ·  ", parts.Where(p => !string.IsNullOrEmpty(p)));
        }
    }

    public string HealthLabel => Health switch
    {
        FirmwareHealth.Verified => "Verified",
        FirmwareHealth.Intact => "Complete",
        FirmwareHealth.Modified => "Modified",
        FirmwareHealth.Damaged => "Damaged",
        _ => "Checking...",
    };

    public bool IsChecking => Health == FirmwareHealth.Checking;
    public bool IsOk => Health == FirmwareHealth.Verified;
    public bool IsWarn => Health is FirmwareHealth.Intact or FirmwareHealth.Modified;
    public bool IsBad => Health == FirmwareHealth.Damaged;
    public bool CanOpen => !IsBad && !IsCurrent;
    public string OpenLabel => IsCurrent ? "In use" : "Open";

    partial void OnHealthChanged(FirmwareHealth value) => NotifyHealth();

    partial void OnIsCurrentChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(OpenLabel));
    }

    void NotifyHealth()
    {
        foreach (var name in new[] { nameof(HealthLabel), nameof(IsChecking), nameof(IsOk), nameof(IsWarn), nameof(IsBad), nameof(CanOpen) })
            OnPropertyChanged(name);
    }

    static string FormatSize(long bytes) =>
        bytes <= 0 ? string.Empty : bytes >= 1L << 30 ? $"{bytes / (1024.0 * 1024 * 1024):0.#} GB" : $"{bytes / (1024.0 * 1024):0.#} MB";
}

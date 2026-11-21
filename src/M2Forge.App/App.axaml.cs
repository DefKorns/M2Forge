using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using M2Forge.App.Services;
using M2Forge.App.ViewModels;
using M2Forge.App.Views;
using System.Linq;

namespace M2Forge.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(new DialogService(window), new SessionStore(AppFolders.Root));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            _ = viewModel.RestoreAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    static void DisableAvaloniaDataAnnotationValidation()
    {
        var plugins = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in plugins)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WeatherDesk.App.ViewModels;
using Avalonia.Styling;
using WeatherDesk.Domain.Models;

namespace WeatherDesk.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Program.Services.GetRequiredService<MainWindowViewModel>();
            vm.Settings.ThemeChanged += theme => RequestedThemeVariant = theme switch
            {
                AppTheme.Dark => ThemeVariant.Dark,
                AppTheme.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };
            desktop.MainWindow = new MainWindow { DataContext = vm };
            _ = vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

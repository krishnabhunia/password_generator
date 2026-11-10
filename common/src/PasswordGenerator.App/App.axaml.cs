using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using PasswordGenerator.App.Services;
using PasswordGenerator.App.ViewModels;
using PasswordGenerator.App.Views;

namespace PasswordGenerator.App;

public partial class App : Application
{
    private static readonly Uri BaseUri = new("avares://PasswordGenerator/");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppState.Load();
            var vm = new MainViewModel(AppState.Settings.Options, BuildInfo.DisplayVersion);
            var window = new MainWindow(vm);
            desktop.MainWindow = window;
            if (AppState.SmokeTestDir is { } dir)
                window.Opened += async (_, _) => desktop.Shutdown(await SmokeTest.RunAsync(window, vm, dir));
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Switches the colours (and light or dark controls) to those of a design.</summary>
    public void ApplyPalette(DesignInfo design)
    {
        var palette = new ResourceInclude(BaseUri) { Source = new Uri($"/Palettes/{design.Palette}.axaml", UriKind.Relative) };
        if (Resources.MergedDictionaries.Count > 0) Resources.MergedDictionaries[0] = palette;
        else Resources.MergedDictionaries.Add(palette);
        RequestedThemeVariant = design.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}

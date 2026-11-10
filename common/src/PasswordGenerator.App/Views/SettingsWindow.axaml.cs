using Avalonia.Controls;
using Avalonia.Interactivity;
using PasswordGenerator.App.Services;

namespace PasswordGenerator.App.Views;

public partial class SettingsWindow : Window
{
    private readonly MainWindow? _main;

    public SettingsWindow() : this(null) { }

    public SettingsWindow(MainWindow? main)
    {
        InitializeComponent();
        _main = main;
        VersionLine.Text = $"Password Generator {BuildInfo.DisplayVersion} · {BuildInfo.Channel} · {BuildInfo.InstallKind}";

        foreach (var design in DesignInfo.All)
        {
            var radio = new RadioButton
            {
                GroupName = "design",
                Content = new TextBlock { Text = design.Name + " — " + design.Description, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                IsChecked = design.Id == AppState.Settings.Design,
            };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && AppState.Settings.Design != design.Id) _main?.ApplyDesign(design.Id);
            };
            DesignList.Children.Add(radio);
        }

        AutoUpdate.IsChecked = AppState.Settings.AutoUpdate;
        IncludeBeta.IsChecked = AppState.Settings.IncludeBeta;
        AutoUpdate.IsCheckedChanged += (_, _) => AppState.Settings = AppState.Settings with { AutoUpdate = AutoUpdate.IsChecked == true };
        IncludeBeta.IsCheckedChanged += (_, _) =>
        {
            AppState.Settings = AppState.Settings with { IncludeBeta = IncludeBeta.IsChecked == true };
            _main?.ChannelChanged();
        };
        Closing += (_, _) => AppState.Save();
    }

    private async void OnCheck(object? sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        CheckButton.IsEnabled = false;
        UpdateStatus.IsVisible = true;
        UpdateStatus.Text = "Checking…";
        try
        {
            UpdateStatus.Text = await _main.CheckForUpdateAsync(quiet: false);
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    private void OnInfo(object? sender, RoutedEventArgs e) => new InfoWindow().ShowDialog(this);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

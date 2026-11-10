using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using PasswordGenerator.App.Services;

namespace PasswordGenerator.App.Views;

public partial class InfoWindow : Window
{
    private readonly IReadOnlyList<InfoRow> _rows = InfoContent.BuildRows();

    public InfoWindow()
    {
        InitializeComponent();
        BuildRows.ItemsSource = _rows;
        FeatureLines.ItemsSource = DocLine.Parse(BuildInfo.ReadDoc("FEATURES.md"));
        NewsLines.ItemsSource = DocLine.Parse(BuildInfo.ReadDoc("CHANGELOG.md"));
        HoodLines.ItemsSource = DocLine.Parse(InfoContent.UnderTheHood);
    }

    public int TabCount => Tabs.ItemCount;

    public void SelectTab(int index) => Tabs.SelectedIndex = index;

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(InfoContent.AsText(_rows));
            CopyText.Text = "Copied";
        }
        catch (Exception)
        {
            CopyText.Text = "Could not copy";
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

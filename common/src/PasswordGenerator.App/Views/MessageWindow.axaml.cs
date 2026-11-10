using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PasswordGenerator.App.Views;

public partial class MessageWindow : Window
{
    public MessageWindow() : this("", "") { }

    public MessageWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        Body.Text = message;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}

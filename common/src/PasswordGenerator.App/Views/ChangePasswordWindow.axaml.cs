using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using PasswordGenerator.App.Services;
using PasswordGenerator.App.ViewModels;

namespace PasswordGenerator.App.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _vm;

    // The XAML loader needs a constructor without arguments.
    public ChangePasswordWindow() : this(new MainViewModel(AppState.Settings.Options, BuildInfo.DisplayVersion)) { }

    public ChangePasswordWindow(MainViewModel main, IKeyboardTyper? typer = null)
    {
        InitializeComponent();
        _vm = new ChangePasswordViewModel(main, AppState.CurrentPasswordStore(), typer ?? KeyboardTyper.Create(), AppState.Settings.TypingOrder);
        DataContext = _vm;
        _vm.ClipboardReader = async () => Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        _vm.OrderChanged += order =>
        {
            AppState.Settings = AppState.Settings with { TypingOrder = order };
            AppState.Save();
        };
        // Coming back from the website or app: that is now "the window used last".
        Activated += (_, _) => _vm.RefreshTarget();
        Closed += (_, _) => _vm.Detach();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }

    public ChangePasswordViewModel ViewModel => _vm;
}

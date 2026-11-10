using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Input;
using PasswordGenerator.App.Services;
using PasswordGenerator.App.ViewModels;

namespace PasswordGenerator.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly UpdateService _updates = new();
    private bool _closingForUpdate;
    private ChangePasswordWindow? _changeWindow;

    // The XAML loader needs a constructor without arguments.
    public MainWindow() : this(new MainViewModel(AppState.Settings.Options, BuildInfo.DisplayVersion)) { }

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.CopyHandler = text => Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : Task.CompletedTask;
        vm.InfoRequested += ShowInfo;
        vm.SettingsRequested += ShowSettings;
        vm.ChangePasswordRequested += () => ShowChangePassword();
        vm.UpdateRequested += async () => await InstallUpdateAsync();
        vm.OptionsChanged += () => AppState.Settings = AppState.Settings with { Options = vm.CurrentOptions() };
        ApplyDesign(AppState.Settings.Design);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F1) ShowInfo();
            else if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control) ShowChangePassword();
            else return;
            e.Handled = true;
        };
        Opened += async (_, _) =>
        {
            if (AppState.SmokeTestDir is not null) return;
            UpdateService.CleanUp();
            await CheckForUpdateAsync(quiet: true);
        };
        Closing += (_, _) =>
        {
            AppState.Save();
            // "Install updates automatically": a downloaded update goes in as the app closes.
            if (_closingForUpdate || !AppState.Settings.AutoUpdate || _updates.Staged is not { } staged) return;
            if (!Core.UpdateFeed.Allowed(staged, AppState.Settings.IncludeBeta)) return; // betas were switched off since
            try { _updates.Apply(relaunch: false); }
            catch (Exception e) { AppState.Log("Update on close failed: " + e.Message); }
        };
    }

    public UpdateService Updates => _updates;

    public void ApplyDesign(string id)
    {
        var design = DesignInfo.Find(id);
        (Avalonia.Application.Current as App)?.ApplyPalette(design);
        Width = design.Width;
        DesignHost.Content = design.CreateView();
        AppState.Settings = AppState.Settings with { Design = design.Id };
    }

    /// <summary>"Also offer beta versions" was changed: a beta that was found or downloaded is dropped.</summary>
    public void ChannelChanged()
    {
        _updates.ApplyChannel(AppState.Settings.IncludeBeta);
        if (_updates.Found is null && !_vm.UpdateBusy) _vm.UpdateText = null;
    }

    /// <summary>Looks for a newer version. Returns a line for the Settings window.</summary>
    public async Task<string> CheckForUpdateAsync(bool quiet)
    {
        var check = await _updates.CheckAsync(AppState.Settings.IncludeBeta);
        if (check.Error is not null) return check.Error;
        if (check.Update is null)
        {
            _vm.UpdateText = null;
            return $"You have the latest version ({BuildInfo.VersionText}).";
        }

        _vm.UpdateText = "Update to v" + check.Update.Version;
        if (AppState.Settings.AutoUpdate)
        {
            try { await _updates.DownloadAsync(check.Update); }
            catch (Exception e) { AppState.Log("Background download failed: " + e.Message); }
        }
        return $"Version {check.Update.Version} is available. Use the \"{_vm.UpdateText}\" button at the top right of the main window.";
    }

    private async Task InstallUpdateAsync()
    {
        if (_updates.Found is not { } update || _vm.UpdateBusy) return;
        var label = _vm.UpdateText;
        _vm.UpdateBusy = true;
        _vm.UpdateText = "Updating…";
        try
        {
            await _updates.DownloadAsync(update);
            AppState.Save();
            _updates.Apply(relaunch: true);
            _closingForUpdate = true;
            Close();
        }
        catch (Exception e)
        {
            AppState.Log("Update failed: " + e);
            _vm.UpdateText = label;
            _vm.UpdateBusy = false;
            await new MessageWindow("Update",
                "The update could not be installed.\n\n" + e.Message + "\n\nYou can download it from the Releases page instead:\n" + Core.UpdateFeed.ReleasesPage)
                .ShowDialog(this);
        }
    }

    /// <summary>Opens the Change a password window, or brings it forward when it is already open.</summary>
    public ChangePasswordWindow ShowChangePassword(IKeyboardTyper? typer = null)
    {
        if (_changeWindow is { } open)
        {
            open.Activate();
            return open;
        }
        var window = new ChangePasswordWindow(_vm, typer);
        window.Closed += (_, _) => _changeWindow = null;
        _changeWindow = window;
        window.Show(this);
        return window;
    }

    private void ShowInfo() => new InfoWindow().ShowDialog(this);

    private void ShowSettings() => new SettingsWindow(this).ShowDialog(this);
}

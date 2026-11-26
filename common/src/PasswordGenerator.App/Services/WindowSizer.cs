using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PasswordGenerator.App.Views;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

/// <summary>
/// Makes a window resizable and maximizable, never smaller than its content needs, and remembers its size,
/// position and maximized state in the settings (<see cref="AppSettings.Windows"/>).
/// </summary>
public sealed class WindowSizer
{
    /// <summary>Room kept for the title bar when a saved size is fitted into a screen.</summary>
    public const double TitleBar = 32;

    private readonly Window _window;
    private readonly NaturalHeightHost? _host;
    private string? _key;
    private double _minWidth;
    private double _minHeight;
    private Size _normalSize;
    private PixelPoint _normalPosition;
    private bool _hasPosition;
    private bool _normalAuto;
    private bool _auto = true;
    private double _wantedHeight;
    private bool _stateChanging;
    private bool _applyOnRestore;
    private int _stateChanges;
    private bool _placed;

    /// <param name="host">The window's content when its height follows the content; null for a window with a fixed smallest height.</param>
    public WindowSizer(Window window, NaturalHeightHost? host = null)
    {
        _window = window;
        _host = host;
        window.CanResize = true;
        // Recorded a moment later: while a window is being maximized it reports its new size before its new state.
        window.Resized += (_, e) =>
        {
            // On Windows every resize the app makes itself is labelled, so an unlabelled one comes from the user
            // too (snapping with Win + arrow). Other systems do not label resizes; there only a labelled drag counts.
            var byUser = e.Reason == WindowResizeReason.User
                || (e.Reason == WindowResizeReason.Unspecified && OperatingSystem.IsWindows());
            RecordLater(byUser);
        };
        window.PositionChanged += (_, _) => RecordLater(byUser: false);
        window.Opened += (_, _) => { EnsureMinimum(); Record(); };
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property != Window.WindowStateProperty) return;
            // Maximizing and restoring resize the window too; those sizes are not the user's own height.
            _stateChanging = true;
            _stateChanges++;
            Dispatcher.UIThread.Post(() =>
            {
                _stateChanging = false;
                if (_applyOnRestore && _window.WindowState == WindowState.Normal) ApplySize();
                else Record();
                EnsureMinimum();
            });
        };
        if (host is not null) host.NaturalHeightChanged += EnsureMinimum;
    }

    /// <summary>
    /// Records size and position a moment later. A resize or move that belongs to maximizing, minimizing or
    /// restoring is left out, whichever the system reports first, the new size or the new state: the handler
    /// of the state change does the recording then.
    /// </summary>
    private void RecordLater(bool byUser)
    {
        var during = _stateChanging;
        var changes = _stateChanges;
        Dispatcher.UIThread.Post(() =>
        {
            if (during || changes != _stateChanges) return;
            if (byUser) UserResized();
            Record();
        });
    }

    /// <summary>
    /// True while the height follows the content, as before the user gave the window a height of their own.
    /// Kept here because Avalonia switches its own SizeToContent off on any resize, also when maximizing.
    /// </summary>
    public bool FollowsContent => _host is not null && _auto;

    /// <summary>The smallest height: what the content needs, or the fixed one given to <see cref="Use"/>.</summary>
    public double SmallestHeight => _host?.NaturalHeight ?? _minHeight;

    /// <summary>
    /// Names the placement this window uses from now on and applies the saved one. The main window calls it
    /// again when the design changes, because every design has its own smallest size.
    /// </summary>
    public void Use(string key, double minWidth, double minHeight = 0)
    {
        if (_key is not null) Save();
        _key = key;
        _minWidth = minWidth;
        _minHeight = minHeight;
        _window.MinWidth = minWidth;
        if (_host is null && minHeight > 0) _window.MinHeight = minHeight;
        Apply(AppState.Settings.Windows is { } all && all.TryGetValue(key, out var placement) ? placement : null);
    }

    /// <summary>Writes the window's placement into the settings (the caller saves the settings file).</summary>
    public void Save()
    {
        if (_key is null) return;
        Record();
        var placement = new WindowPlacement
        {
            X = _normalPosition.X,
            Y = _normalPosition.Y,
            HasPosition = _hasPosition,
            Width = _normalSize.Width,
            Height = _normalAuto ? 0 : SavedHeight(),
            Maximized = _window.WindowState == WindowState.Maximized,
        };
        AppState.Settings = AppState.Settings with { Windows = WindowPlacements.With(AppState.Settings.Windows, _key, placement) };
    }

    /// <summary>Gives the window a size as a drag of its edges would, never below the smallest size.</summary>
    public void Resize(double width, double height)
    {
        if (_window.WindowState != WindowState.Normal) _window.WindowState = WindowState.Normal;
        _applyOnRestore = false;
        _auto = false;
        _wantedHeight = height;
        _window.SizeToContent = SizeToContent.Manual;
        _window.Width = Math.Max(width, _minWidth);
        _window.Height = Math.Max(height, SmallestHeight);
    }

    /// <summary>Forgets the saved placement and goes back to the smallest size.</summary>
    public void Reset()
    {
        if (_key is null) return;
        AppState.Settings = AppState.Settings with { Windows = WindowPlacements.With(AppState.Settings.Windows, _key, null) };
        if (_window.WindowState != WindowState.Normal) _window.WindowState = WindowState.Normal;
        Apply(null);
    }

    private void Apply(WindowPlacement? saved)
    {
        var screens = Screens();
        var width = Math.Max(saved?.Width ?? 0, _minWidth);
        var height = saved?.Height ?? 0; // 0: the height follows the content
        if (_host is null) height = Math.Max(height, _minHeight);
        // The screen is chosen with the window's real width, the same way the position is judged.
        var home = saved is null ? null : WindowPlacements.ScreenFor(saved, width, screens);

        // A size saved on a larger screen must not be larger than the screen it opens on now.
        if (screens.Count > 0)
        {
            var fitted = WindowPlacements.FitInto(width, height + TitleBar, home ?? screens[0]);
            width = Math.Max(fitted.Width, _minWidth);
            if (height > 0) height = Math.Max(fitted.Height - TitleBar, _host is null ? _minHeight : 0);
        }

        // What the window goes back to when it is not maximized. This is also what gets saved, so a design
        // chosen while the window is maximized keeps its own remembered size.
        _auto = _host is not null && height <= 0;
        _wantedHeight = height;
        _normalSize = new Size(width, height);
        _normalAuto = _auto;

        // A maximized window stays maximized; the size is applied when it is restored.
        if (_placed && _window.WindowState != WindowState.Normal) _applyOnRestore = true;
        else ApplySize();

        if (_placed) return;
        _placed = true;
        if (home is not null)
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Position = new PixelPoint(saved!.X, saved.Y);
            _normalPosition = _window.Position;
            _hasPosition = true;
        }
        if (saved?.Maximized == true) _window.WindowState = WindowState.Maximized;
    }

    /// <summary>Gives the window the size it should have when it is not maximized.</summary>
    private void ApplySize()
    {
        _applyOnRestore = false;
        if (_auto)
        {
            _window.Width = _normalSize.Width;
            FollowContent();
            return;
        }
        _window.SizeToContent = SizeToContent.Manual;
        _window.Width = _normalSize.Width;
        // The content's own height may not be known yet (a new design is not laid out); EnsureMinimum corrects it after the layout.
        _window.Height = Math.Max(_wantedHeight, _host is null ? _minHeight : 0);
    }

    /// <summary>Remembers size and position while the window is neither maximized nor minimized.</summary>
    private void Record()
    {
        // While a size waits to be applied on restore, the fields already hold what must be saved.
        if (_applyOnRestore || !_window.IsVisible || _window.WindowState != WindowState.Normal) return;
        _normalSize = _window.ClientSize;
        _normalPosition = _window.Position;
        _hasPosition = true;
        _normalAuto = FollowsContent;
    }

    /// <summary>The user dragged an edge: a height other than the one the window had becomes the user's own height.</summary>
    private void UserResized()
    {
        if (_stateChanging || !_window.IsVisible || _window.WindowState != WindowState.Normal) return;
        var height = _window.ClientSize.Height;
        if (Math.Abs(height - TargetHeight()) <= 1) return; // only the width changed
        _auto = false;
        _wantedHeight = height;
    }

    /// <summary>
    /// The height the window should have now: the content's while it follows the content, otherwise the user's
    /// own height, or more while the content needs more (a long typing order, a 64-character password).
    /// </summary>
    private double TargetHeight() => FollowsContent ? SmallestHeight : Math.Max(_wantedHeight, SmallestHeight);

    /// <summary>The user's own height; the real height only where the system resized the window without saying why.</summary>
    private double SavedHeight() =>
        _wantedHeight > 0 && Math.Abs(_normalSize.Height - Math.Max(_wantedHeight, SmallestHeight)) <= 1 ? _wantedHeight : _normalSize.Height;

    /// <summary>Lets the height follow the content (again).</summary>
    private void FollowContent()
    {
        if (_window.SizeToContent.HasFlag(SizeToContent.Height)) return;
        _window.SizeToContent = SizeToContent.Height;
        _window.InvalidateMeasure();
    }

    /// <summary>Keeps the window at least as large as its content needs.</summary>
    private void EnsureMinimum()
    {
        var smallest = SmallestHeight;
        if (smallest > 0 && Math.Abs(_window.MinHeight - smallest) > 0.01) _window.MinHeight = smallest;
        if (_window.WindowState != WindowState.Normal) return;
        if (!double.IsNaN(_window.Width) && _window.Width < _minWidth - 0.5) _window.Width = _minWidth;
        if (FollowsContent)
        {
            FollowContent();
            return;
        }
        // Grows while the content needs more than the user's height, and goes back to it afterwards.
        var target = TargetHeight();
        if (target > 0 && !double.IsNaN(_window.Height) && Math.Abs(_window.Height - target) > 0.5) _window.Height = target;
    }

    private IReadOnlyList<ScreenArea> Screens()
    {
        try
        {
            var all = _window.Screens.All;
            var primary = _window.Screens.Primary;
            // The primary screen first: a window without a usable position opens there.
            return all.OrderBy(s => ReferenceEquals(s, primary) ? 0 : 1)
                .Select(s => new ScreenArea(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height, s.Scaling))
                .ToArray();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            return Array.Empty<ScreenArea>();
        }
    }
}

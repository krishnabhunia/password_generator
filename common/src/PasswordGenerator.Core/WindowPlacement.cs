namespace PasswordGenerator.Core;

/// <summary>
/// Where a window was when it was last closed. Position is in screen pixels, size in the window's own units.
/// Height 0 means the height follows the content, as it does before the user resizes the window.
/// </summary>
public sealed record WindowPlacement
{
    public int X { get; init; }
    public int Y { get; init; }
    /// <summary>False until the window was shown once; it then opens centred.</summary>
    public bool HasPosition { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool Maximized { get; init; }

    /// <summary>The same placement with impossible values removed; a damaged settings file can hold anything.</summary>
    public WindowPlacement Normalized() => this with
    {
        X = Math.Clamp(X, -WindowPlacements.MaxCoordinate, WindowPlacements.MaxCoordinate),
        Y = Math.Clamp(Y, -WindowPlacements.MaxCoordinate, WindowPlacements.MaxCoordinate),
        Width = WindowPlacements.Size(Width),
        Height = WindowPlacements.Size(Height),
    };
}

/// <summary>The part of a screen windows may use (without the taskbar), in pixels, and its scaling (1.0 = 100 %).</summary>
public readonly record struct ScreenArea(int X, int Y, int Width, int Height, double Scaling = 1);

public static class WindowPlacements
{
    public const int MaxCoordinate = 100_000;
    public const double MaxSize = 20_000;
    /// <summary>The generated password's text grows with the window up to this many times its size.</summary>
    public const double MaxTextScale = 1.5;

    /// <summary>The names the main window's placements are saved under, one for each design.</summary>
    public static string MainKey(string design) => "Main." + design;
    public const string ChangeKey = "Change";
    public const string InfoKey = "Info";

    internal static double Size(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, MaxSize) : 0;

    /// <summary>
    /// True when a window at the saved position can be seen and moved on one of the screens: its title bar
    /// is inside a screen from top to bottom, and a good piece of it from left to right. A window saved on a
    /// monitor that is no longer there fails this and opens centred.
    /// </summary>
    public static bool IsReachable(WindowPlacement placement, double width, IEnumerable<ScreenArea> screens) =>
        ScreenFor(placement, width, screens) is not null;

    /// <summary>The first screen on which a window of this width at the saved position can be reached; null when there is none.</summary>
    public static ScreenArea? ScreenFor(WindowPlacement placement, double width, IEnumerable<ScreenArea> screens)
    {
        if (!placement.HasPosition) return null;
        foreach (var screen in screens)
        {
            var scale = screen.Scaling > 0 && double.IsFinite(screen.Scaling) ? screen.Scaling : 1;
            var titleBar = (int)Math.Ceiling(40 * scale);
            var needed = (int)Math.Ceiling(Math.Min(120, Math.Max(width, 1)) * scale);
            var slack = (int)Math.Ceiling(12 * scale); // a maximized or snapped window sits a few pixels outside the screen
            long left = Math.Max(placement.X, screen.X);
            long right = Math.Min(placement.X + (long)Math.Ceiling(Math.Max(width, 1) * scale), screen.X + (long)screen.Width);
            if (right - left < needed) continue;
            if (placement.Y < screen.Y - slack || placement.Y > screen.Y + (long)screen.Height - titleBar) continue;
            return screen;
        }
        return null;
    }

    /// <summary>The largest size (in the window's own units) that still fits the screen the window opens on.</summary>
    public static (double Width, double Height) FitInto(double width, double height, ScreenArea screen)
    {
        var scale = screen.Scaling > 0 && double.IsFinite(screen.Scaling) ? screen.Scaling : 1;
        return (Math.Min(width, screen.Width / scale), Math.Min(height, screen.Height / scale));
    }

    /// <summary>How much larger the password's text is shown in a window of this width: 1 at today's width, up to 1.5.</summary>
    public static double TextScale(double width, double smallestWidth)
    {
        if (!(smallestWidth > 0) || !double.IsFinite(width)) return 1;
        return Math.Clamp(width / smallestWidth, 1, MaxTextScale);
    }

    /// <summary>A font size grown by <see cref="TextScale"/>, in half steps so it does not flicker while dragging.</summary>
    public static double TextSize(double baseSize, double width, double smallestWidth) =>
        Math.Round(baseSize * TextScale(width, smallestWidth) * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>The saved placements with one added or replaced.</summary>
    public static IReadOnlyDictionary<string, WindowPlacement> With(IReadOnlyDictionary<string, WindowPlacement>? all, string key, WindowPlacement? placement)
    {
        var map = all is null ? new Dictionary<string, WindowPlacement>() : new Dictionary<string, WindowPlacement>(all);
        if (placement is null) map.Remove(key);
        else map[key] = placement.Normalized();
        return map;
    }
}

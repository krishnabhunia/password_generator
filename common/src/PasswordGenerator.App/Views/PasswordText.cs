using Avalonia.Controls;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.Views;

/// <summary>Lets the generated password's text grow with the window, up to 1.5 times its size.</summary>
internal static class PasswordText
{
    /// <param name="view">The design's view; its width is the window's width.</param>
    /// <param name="list">The control that shows the password's characters; they inherit its font size.</param>
    /// <param name="baseSize">The font size at the design's smallest width.</param>
    /// <param name="baseWidth">The design's smallest width.</param>
    public static void Follow(Control view, ItemsControl list, double baseSize, double baseWidth)
    {
        list.FontSize = baseSize;
        view.SizeChanged += (_, e) =>
        {
            var size = WindowPlacements.TextSize(baseSize, e.NewSize.Width, baseWidth);
            if (Math.Abs(list.FontSize - size) > 0.01) list.FontSize = size;
        };
    }
}

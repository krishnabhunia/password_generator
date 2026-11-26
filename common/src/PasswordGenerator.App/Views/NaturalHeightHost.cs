using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PasswordGenerator.App.Views;

/// <summary>
/// Holds a resizable window's content and knows how high that content needs to be at the window's width
/// (its "natural" height). The window uses it as its smallest height, so nothing can be cut off by resizing.
/// </summary>
public sealed class NaturalHeightHost : Decorator
{
    /// <summary>The height the content needs at the current width.</summary>
    public double NaturalHeight { get; private set; }

    /// <summary>Raised (after the layout pass) when <see cref="NaturalHeight"/> changed.</summary>
    public event Action? NaturalHeightChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child) return default;
        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var natural = child.DesiredSize.Height;
        if (Math.Abs(natural - NaturalHeight) > 0.01)
        {
            NaturalHeight = natural;
            Dispatcher.UIThread.Post(() => NaturalHeightChanged?.Invoke());
        }
        return new Size(child.DesiredSize.Width, double.IsInfinity(availableSize.Height) ? natural : Math.Min(natural, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(finalSize.Height, NaturalHeight)));
        return finalSize;
    }
}

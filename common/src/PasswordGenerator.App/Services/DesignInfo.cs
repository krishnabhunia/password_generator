using Avalonia.Controls;
using PasswordGenerator.App.Views;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

/// <summary>One of the looks the user can pick in Settings.</summary>
public sealed record DesignInfo(string Id, string Name, string Description, string Palette, bool Dark, double Width, Func<Control> CreateView)
{
    public static readonly IReadOnlyList<DesignInfo> All = new[]
    {
        new DesignInfo("TwoPaneLight", "Two-pane Light", "choices left, password right (default)", "Light", false, 980, () => new TwoPaneView()),
        new DesignInfo("TwoPaneDark", "Two-pane Dark", "the same layout, dark colours", "Dark", true, 980, () => new TwoPaneView()),
        new DesignInfo("FluentLight", "Fluent Light", "one column of cards", "Light", false, 560, () => new FluentView()),
        new DesignInfo("Compact", "Compact", "a small window with element tiles", "Compact", false, 420, () => new CompactView()),
    };

    public static DesignInfo Find(string? id) =>
        All.FirstOrDefault(d => d.Id == id) ?? All.First(d => d.Id == AppSettings.DefaultDesign);
}

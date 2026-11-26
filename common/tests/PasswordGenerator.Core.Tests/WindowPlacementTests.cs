using System.Text.Json;
using PasswordGenerator.Core;
using Xunit;

namespace PasswordGenerator.Core.Tests;

public class WindowPlacementTests
{
    private static readonly ScreenArea Laptop = new(0, 0, 1920, 1040);
    private static readonly ScreenArea Second = new(1920, 0, 2560, 1400, 1.5);

    private static WindowPlacement At(int x, int y) => new() { X = x, Y = y, HasPosition = true, Width = 980, Height = 600 };

    [Theory]
    [InlineData(980, 980, 1.0)]
    [InlineData(500, 980, 1.0)]
    [InlineData(1225, 980, 1.25)]
    [InlineData(1470, 980, 1.5)]
    [InlineData(1920, 980, 1.5)]
    [InlineData(630, 420, 1.5)]
    [InlineData(double.NaN, 980, 1.0)]
    [InlineData(1200, 0, 1.0)]
    public void Password_text_grows_with_the_window_up_to_one_and_a_half_times(double width, double smallest, double expected)
    {
        Assert.Equal(expected, WindowPlacements.TextScale(width, smallest), 3);
    }

    [Fact]
    public void Text_size_moves_in_half_steps()
    {
        Assert.Equal(28, WindowPlacements.TextSize(28, 980, 980));
        Assert.Equal(29.5, WindowPlacements.TextSize(28, 1024, 980));
        Assert.Equal(42, WindowPlacements.TextSize(28, 3000, 980));
        Assert.Equal(27, WindowPlacements.TextSize(18, 740, 420));
    }

    [Fact]
    public void A_window_on_a_screen_keeps_its_position()
    {
        Assert.True(WindowPlacements.IsReachable(At(100, 80), 980, new[] { Laptop }));
        Assert.True(WindowPlacements.IsReachable(At(-8, -8), 980, new[] { Laptop }));      // snapped to the top left
        Assert.True(WindowPlacements.IsReachable(At(2500, 300), 980, new[] { Laptop, Second }));
        Assert.True(WindowPlacements.IsReachable(At(1700, 300), 980, new[] { Laptop }));   // 220 pixels still on screen
    }

    [Fact]
    public void A_window_that_cannot_be_reached_opens_centred()
    {
        Assert.False(WindowPlacements.IsReachable(At(2500, 300), 980, new[] { Laptop }));   // the second monitor is gone
        Assert.False(WindowPlacements.IsReachable(At(1900, 300), 980, new[] { Laptop }));   // only 20 pixels on screen
        Assert.False(WindowPlacements.IsReachable(At(100, 1030), 980, new[] { Laptop }));   // title bar below the screen
        Assert.False(WindowPlacements.IsReachable(At(100, -200), 980, new[] { Laptop }));   // title bar above the screen
        Assert.False(WindowPlacements.IsReachable(At(-32000, -32000), 980, new[] { Laptop })); // saved while minimized
        Assert.False(WindowPlacements.IsReachable(At(100, 80), 980, Array.Empty<ScreenArea>()));
        Assert.False(WindowPlacements.IsReachable(At(100, 80) with { HasPosition = false }, 980, new[] { Laptop }));
    }

    [Fact]
    public void Scaling_counts_when_checking_the_position()
    {
        // 980 units are 1470 pixels at 150 %: the window reaches 250 pixels into the second screen.
        Assert.True(WindowPlacements.IsReachable(At(700, 300), 980, new[] { Second }));
        Assert.False(WindowPlacements.IsReachable(At(500, 300), 980, new[] { Second }));
    }

    [Fact]
    public void A_saved_size_is_fitted_into_a_smaller_screen()
    {
        Assert.Equal((1920, 1040), WindowPlacements.FitInto(2400, 1300, Laptop));
        Assert.Equal((1300, 700), WindowPlacements.FitInto(1300, 700, Laptop));
        var fitted = WindowPlacements.FitInto(2400, 1300, Second);
        Assert.Equal(2560 / 1.5, fitted.Width, 3);
        Assert.Equal(1400 / 1.5, fitted.Height, 3);
    }

    [Fact]
    public void Damaged_values_are_brought_into_range()
    {
        var p = new WindowPlacement { X = int.MaxValue, Y = int.MinValue, Width = double.NaN, Height = -50, Maximized = true }.Normalized();
        Assert.Equal(WindowPlacements.MaxCoordinate, p.X);
        Assert.Equal(-WindowPlacements.MaxCoordinate, p.Y);
        Assert.Equal(0, p.Width);
        Assert.Equal(0, p.Height);
        Assert.True(p.Maximized);
        Assert.Equal(WindowPlacements.MaxSize, new WindowPlacement { Width = 1e9 }.Normalized().Width);
        Assert.Equal(0, new WindowPlacement { Height = double.PositiveInfinity }.Normalized().Height);
    }

    [Fact]
    public void Placements_are_added_replaced_and_removed_by_name()
    {
        var one = WindowPlacements.With(null, "Change", new WindowPlacement { Width = 1200, Height = 600 });
        var two = WindowPlacements.With(one, WindowPlacements.MainKey("Compact"), new WindowPlacement { Width = 500 });
        var three = WindowPlacements.With(two, "Change", new WindowPlacement { Width = 1300, Maximized = true });
        Assert.Single(one);
        Assert.Equal(2, three.Count);
        Assert.Equal(1300, three["Change"].Width);
        Assert.True(three["Change"].Maximized);
        Assert.Equal(500, three["Main.Compact"].Width);
        Assert.Equal(1200, one["Change"].Width); // the earlier map is untouched
        Assert.False(WindowPlacements.With(three, "Change", null).ContainsKey("Change"));
    }

    [Fact]
    public void Settings_keep_the_placements_and_old_files_have_none()
    {
        Assert.Null(new AppSettings().Windows);
        Assert.Null(JsonSerializer.Deserialize<AppSettings>("{\"Design\":\"TwoPaneLight\"}")!.Normalized().Windows);

        var path = Path.Combine(Path.GetTempPath(), "pwg-placement-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings
            {
                Windows = WindowPlacements.With(null, "Main.TwoPaneLight",
                    new WindowPlacement { X = 40, Y = 60, HasPosition = true, Width = 1300, Height = 0, Maximized = true }),
            };
            Assert.True(SettingsStore.Save(path, settings));
            var loaded = SettingsStore.Load(path).Windows!["Main.TwoPaneLight"];
            Assert.Equal(new WindowPlacement { X = 40, Y = 60, HasPosition = true, Width = 1300, Height = 0, Maximized = true }, loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_damaged_placement_does_not_lose_the_other_settings()
    {
        var json = "{\"Design\":\"Compact\",\"Windows\":{\"Change\":{\"Width\":-5,\"Height\":1e300,\"X\":999999999},\"Info\":null}}";
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!.Normalized();
        Assert.Equal("Compact", settings.Design);
        Assert.Equal(0, settings.Windows!["Change"].Width);
        Assert.Equal(WindowPlacements.MaxSize, settings.Windows["Change"].Height);
        Assert.Equal(WindowPlacements.MaxCoordinate, settings.Windows["Change"].X);
        Assert.False(settings.Windows.ContainsKey("Info"));
    }
}

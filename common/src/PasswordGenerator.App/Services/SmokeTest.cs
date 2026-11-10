using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using PasswordGenerator.App.ViewModels;
using PasswordGenerator.App.Views;

namespace PasswordGenerator.App.Services;

/// <summary>
/// Self-test used by the pipeline: opens every design and window, checks that nothing is cut off,
/// and saves a picture of each. Start the app with --smoke-test &lt;folder&gt;.
/// </summary>
/// <summary>
/// A fixed, repeatable sequence for the self-test. It is NOT random and must never be used for a real password:
/// the pictures the self-test saves and publishes show only these sample passwords.
/// </summary>
public sealed class SampleRandomSource : PasswordGenerator.Core.IRandomSource
{
    private uint _state = 20261110;

    public int Next(int exclusiveMax)
    {
        _state = _state * 1664525 + 1013904223;
        return (int)((_state >> 8) % (uint)exclusiveMax);
    }
}

public static class SmokeTest
{
    private const double MaxHeight = 900; // must fit a 1080-pixel-high screen with the taskbar

    public static async Task<int> RunAsync(MainWindow window, MainViewModel vm, string dir)
    {
        var log = new StringBuilder();
        var failures = new List<string>();
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var design in DesignInfo.All)
            {
                window.ApplyDesign(design.Id);
                Reset(vm);
                await Settle();
                var normal = Check(window, design.Id, failures);
                log.AppendLine($"{design.Id}: {window.ClientSize.Width:0}x{normal:0}");
                Save(window, Path.Combine(dir, design.Id + ".png"));

                // Longest password with the highest minimums: the window must not change height.
                vm.Length = 64;
                foreach (var e in vm.AllElements) e.Min = 9;
                await Settle();
                var longest = Check(window, design.Id + " (64 characters)", failures);
                if (Math.Abs(longest - normal) > 0.5) failures.Add($"{design.Id}: height changes from {normal:0} to {longest:0} with a 64-character password");
                if (vm.Password.Length != 64) failures.Add($"{design.Id}: expected 64 characters, got {vm.Password.Length}");
                Save(window, Path.Combine(dir, design.Id + "-64.png"));

                // Nothing switched on: the hint is shown instead of a password.
                foreach (var e in vm.AllElements) e.IsOn = false;
                await Settle();
                Check(window, design.Id + " (nothing on)", failures);
                if (!vm.IsEmpty) failures.Add($"{design.Id}: a password was made with every element off");
                Save(window, Path.Combine(dir, design.Id + "-empty.png"));

                // A few special characters only, to show chips in both states.
                Reset(vm);
                vm.ClearSymbolsCommand.Execute(null);
                foreach (var s in vm.Symbols.Take(5)) s.ToggleCommand.Execute(null);
                vm.Digits.IsOn = false;
                await Settle();
                Check(window, design.Id + " (some chips)", failures);
                if (vm.Password.Any(char.IsDigit)) failures.Add($"{design.Id}: digits appear although they are off");
                Save(window, Path.Combine(dir, design.Id + "-chips.png"));
            }

            // The update button at the top right.
            window.ApplyDesign(DesignInfo.All[0].Id);
            Reset(vm);
            vm.UpdateText = "Update to v9.9.9";
            await Settle();
            Check(window, "update button", failures);
            Save(window, Path.Combine(dir, "update-button.png"));
            vm.UpdateText = null;

            foreach (var design in new[] { DesignInfo.All[0], DesignInfo.All[1] })
            {
                window.ApplyDesign(design.Id);
                await Settle();
                var settings = new SettingsWindow(window);
                settings.Show();
                await Settle();
                Check(settings, "Settings " + design.Id, failures);
                Save(settings, Path.Combine(dir, $"settings-{design.Id}.png"));
                settings.Close();

                var info = new InfoWindow();
                info.Show();
                for (var tab = 0; tab < info.TabCount; tab++)
                {
                    info.SelectTab(tab);
                    await Settle();
                    Save(info, Path.Combine(dir, $"info-{design.Id}-{tab + 1}.png"));
                }
                if (info.TabCount != 4) failures.Add("The Info window should have 4 tabs, it has " + info.TabCount);
                info.Close();
            }
            window.ApplyDesign(DesignInfo.All[0].Id);
        }
        catch (Exception e)
        {
            failures.Add("Exception: " + e);
        }

        var result = (failures.Count == 0 ? "PASSED" : "FAILED") + Environment.NewLine + log + string.Join(Environment.NewLine, failures);
        File.WriteAllText(Path.Combine(dir, "smoke-result.txt"), result);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Reset(MainViewModel vm)
    {
        foreach (var e in vm.AllElements) { e.IsOn = true; e.Min = 1; }
        vm.SelectAllSymbolsCommand.Execute(null);
        vm.Length = 16;
    }

    private static Task Settle() => Task.Delay(350);

    /// <summary>Fails when a control sticks out of the window or got less room than it needs. Returns the window height.</summary>
    private static double Check(Window window, string name, List<string> failures)
    {
        var size = window.ClientSize;
        if (size.Height > MaxHeight) failures.Add($"{name}: the window is {size.Height:0} high, more than {MaxHeight:0}");
        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            if (control is not (TextBlock or Button or ToggleSwitch or Slider or ProgressBar or CheckBox or RadioButton)) continue;
            if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0) continue;
            if (control.FindAncestorOfType<ScrollViewer>() is not null) continue; // scrolls, so it may be off screen
            var origin = control.TranslatePoint(new Point(0, 0), window);
            if (origin is not { } p) continue;
            var what = $"{name}: {control.GetType().Name} \"{Describe(control)}\"";
            if (p.X < -0.5 || p.Y < -0.5 || p.X + control.Bounds.Width > size.Width + 0.5 || p.Y + control.Bounds.Height > size.Height + 0.5)
                failures.Add($"{what} is outside the window");
            if (control is TextBlock && Overflows(control) is { } holder)
                failures.Add($"{what} runs past the edge of its {holder}");
            var needed = control.DesiredSize.Width - control.Margin.Left - control.Margin.Right;
            if (control is TextBlock { TextWrapping: Avalonia.Media.TextWrapping.NoWrap } tb && tb.TextTrimming == Avalonia.Media.TextTrimming.None && needed > control.Bounds.Width + 1)
                failures.Add($"{what} is cut off (needs {needed:0}, has {control.Bounds.Width:0})");
        }
        return size.Height;
    }

    /// <summary>The first container whose edge the control crosses by more than a few pixels, or null.</summary>
    private static string? Overflows(Control control)
    {
        const double tolerance = 4; // chip rows use a small negative margin on purpose
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
        {
            if (ancestor is Window) break;
            if (control.TranslatePoint(new Point(0, 0), ancestor) is not { } p) continue;
            if (p.X < -tolerance || p.Y < -tolerance
                || p.X + control.Bounds.Width > ancestor.Bounds.Width + tolerance
                || p.Y + control.Bounds.Height > ancestor.Bounds.Height + tolerance)
                return ancestor.GetType().Name;
        }
        return null;
    }

    private static string Describe(Control control) => control switch
    {
        TextBlock t => t.Text ?? "",
        ContentControl { Content: string s } => s,
        _ => control.Name ?? "",
    };

    private static void Save(Window window, string path)
    {
        var size = window.ClientSize;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(size.Width * 2), (int)(size.Height * 2)), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}

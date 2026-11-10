using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PasswordGenerator.Core;
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

/// <summary>Self-test stand-in for the typer: names a sample window and never types anything.</summary>
public sealed class SampleTyper : IKeyboardTyper
{
    public bool Available => true;
    public TargetWindow? Target { get; set; } = new(new IntPtr(1), "Change your password – Sample window");
    public int TimesTyped { get; private set; }
    public IntPtr LastHandle { get; private set; }
    public IReadOnlyList<TypingAction> LastPlan { get; private set; } = Array.Empty<TypingAction>();
    public TypingDelays? LastDelays { get; private set; }

    public TargetWindow? FindTarget() => Target;

    public Task<TypeResult> TypeAsync(IntPtr target, IReadOnlyList<TypingAction> plan, TypingDelays delays, CancellationToken ct = default)
    {
        TimesTyped++;
        LastHandle = target;
        LastPlan = plan;
        LastDelays = delays;
        return Task.FromResult(new TypeResult(true, ""));
    }
}

/// <summary>A window with three boxes that the self-test types into, to prove the typing really arrives.</summary>
public sealed class TypingProbeWindow : Window
{
    public TextBox First { get; } = new();
    public TextBox Second { get; } = new();
    public TextBox Third { get; } = new();
    public bool EnterPressed { get; private set; }

    public TypingProbeWindow()
    {
        Title = "Password Generator typing probe";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        Content = new StackPanel { Margin = new Thickness(16), Spacing = 8, Children = { First, Second, Third } };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter) EnterPressed = true; }, RoutingStrategies.Tunnel);
    }
}

public static class SmokeTest
{
    // Every window must fit a 768-pixel-high screen with the taskbar and title bar (about 749 usable).
    // A taller window is silently cut off there, which is how the build machine caught one.
    private const double MaxHeight = 740;

    public static async Task<int> RunAsync(MainWindow window, MainViewModel vm, string dir)
    {
        var log = new StringBuilder();
        var failures = new List<string>();
        try
        {
            Directory.CreateDirectory(dir);
            if (string.Equals(Path.GetFullPath(AppState.DataDir), Path.GetFullPath(AppState.DefaultDataDir), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The self-test must not run on the user's real data folder.");
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
            // The Change a password window, in a light and a dark design.
            const string sampleCurrent = "Old-Sample-1!";
            foreach (var design in new[] { DesignInfo.All[0], DesignInfo.All[1] })
            {
                window.ApplyDesign(design.Id);
                Reset(vm);
                await Settle();
                var change = window.ShowChangePassword(new SampleTyper());
                var cvm = change.ViewModel;
                cvm.Current = "";
                await Settle();
                Check(change, "Change a password (nothing pasted) " + design.Id, failures);
                if (cvm.CanType) failures.Add("Typing is offered although no current password was pasted");
                Save(change, Path.Combine(dir, $"change-{design.Id}-empty.png"));

                cvm.Current = sampleCurrent;
                await Settle();
                var height = Check(change, "Change a password " + design.Id, failures);
                if (!cvm.CanType) failures.Add("Typing is not offered although everything is filled in: " + cvm.Problem);
                Save(change, Path.Combine(dir, $"change-{design.Id}.png"));

                // Everything shown and the longest order, to see the chips wrap.
                cvm.ShowCurrent = true;
                cvm.ShowNew = true;
                while (cvm.Steps.Count < TypingOrder.MaxSteps) cvm.Adders[cvm.Steps.Count % cvm.Adders.Count].Command.Execute(null);
                await Settle();
                // The window grows with a long order; Check makes sure it still fits the screen and nothing is cut off.
                var tallest = Check(change, "Change a password (12 steps) " + design.Id, failures);
                Save(change, Path.Combine(dir, $"change-{design.Id}-shown.png"));
                cvm.ResetCommand.Execute(null);

                // After typing: one line of feedback, and nothing else changes.
                cvm.TypeCommand.Execute(null);
                await Settle();
                Check(change, "Change a password (after typing) " + design.Id, failures);
                if (!cvm.IsIdle || !cvm.ShowLastTyped) failures.Add("After typing the window should be ready again and say what was typed");
                Save(change, Path.Combine(dir, $"change-{design.Id}-typed.png"));
                log.AppendLine($"Change a password {design.Id}: {change.ClientSize.Width:0}x{height:0} ({tallest:0} high with 12 steps)");

                // Closing and opening again stands in for a restart: the current password must come back (Windows only).
                cvm.Current = sampleCurrent;
                change.Close();
                await Settle();
                var again = window.ShowChangePassword(new SampleTyper());
                var expected = OperatingSystem.IsWindows() ? sampleCurrent : "";
                if (again.ViewModel.Current != expected) failures.Add("The current password did not come back after reopening the window");
                again.ViewModel.Current = "";
                again.Close();
                await Settle();
            }
            await ChangeRules(window, vm, failures, log);
            if (File.Exists(AppState.CurrentPasswordPath)) failures.Add("Forgetting the current password left its file behind");
            log.AppendLine(OperatingSystem.IsWindows() ? "Encrypted store: saved, read back and removed" : "Encrypted store: not available on this system, nothing written");

            await TypingCheck(failures, log);
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

    /// <summary>The rules of the Change a password window.</summary>
    private static async Task ChangeRules(MainWindow window, MainViewModel vm, List<string> failures, StringBuilder log)
    {
        const string sampleCurrent = "Old-Sample-1!";
        var typer = new SampleTyper();
        var change = window.ShowChangePassword(typer);
        var cvm = change.ViewModel;
        cvm.Current = sampleCurrent;
        await Settle();

        // 1. A different window is in front than the one named on screen: nothing may be typed.
        typer.Target = new TargetWindow(new IntPtr(2), "Some other window");
        cvm.TypeCommand.Execute(null);
        await Settle();
        if (typer.TimesTyped != 0) failures.Add("Change rules: typed into a window other than the one that was shown");
        if (!cvm.Problem.Contains("changed") || !cvm.TargetTitle.Contains("Some other window"))
            failures.Add("Change rules: a changed target window was not reported: " + cvm.Problem);

        // 2. The delays chosen in the window are the ones the typer gets, and they are remembered.
        cvm.CharDelayMs = 35;
        cvm.TabDelayMs = 250;
        cvm.CharDelayUpCommand.Execute(null);   // 40
        cvm.TabDelayDownCommand.Execute(null);  // 200
        var typed = vm.Password;
        cvm.TypeCommand.Execute(null);
        await Settle();
        if (typer.TimesTyped != 1 || typer.LastHandle != new IntPtr(2)) failures.Add("Change rules: typing did not run once into the shown window");
        if (typer.LastPlan.OfType<TypingAction.Text>().Count(t => t.Value == typed) != 2) failures.Add("Change rules: the typed plan does not carry the new password twice");
        if (typer.LastDelays != new TypingDelays(40, 200)) failures.Add($"Change rules: the typer got delays {typer.LastDelays}, expected 40 ms and 200 ms");
        if (AppState.Settings.Delays() != new TypingDelays(40, 200)) failures.Add("Change rules: the delays were not saved in the settings");
        cvm.CharDelayMs = 99999;
        cvm.TabDelayMs = -5;
        if (cvm.CharDelayMs != TypingDelays.MaxCharMs || cvm.TabDelayMs != 0) failures.Add("Change rules: the delays are not kept inside their limits");

        // 3. Typing changes nothing: the current password and the new password are both as they were.
        if (cvm.Current != sampleCurrent) failures.Add("Change rules: the current password changed without the user changing it");
        if (vm.Password != typed || cvm.NewPassword != typed) failures.Add("Change rules: the new password was replaced after typing");
        if (!cvm.IsIdle || !cvm.LastTyped.Contains("was not changed")) failures.Add("Change rules: after typing the window is not ready again: " + cvm.LastTyped);

        // 4. Only the user changes the current password; New changes only the new one.
        cvm.NewCommand.Execute(null);
        if (vm.Password == typed) failures.Add("Change rules: New did not make another new password");
        if (cvm.Current != sampleCurrent) failures.Add("Change rules: New changed the current password");

        // 5. The window closes whenever the user wants, and the current password is still there afterwards.
        change.Close();
        await Settle();
        if (change.IsVisible) failures.Add("Change rules: the window did not close");
        var again = window.ShowChangePassword(new SampleTyper());
        var kept = OperatingSystem.IsWindows() ? sampleCurrent : "";
        if (again.ViewModel.Current != kept) failures.Add("Change rules: the current password was not kept after closing the window");
        if (again.ViewModel.Delays != new TypingDelays(TypingDelays.MaxCharMs, 0)) failures.Add("Change rules: the delays did not come back after reopening the window");
        again.ViewModel.CharDelayMs = TypingDelays.DefaultCharMs;
        again.ViewModel.TabDelayMs = TypingDelays.DefaultKeyMs;
        again.ViewModel.Current = "";
        again.Close();
        await Settle();
        log.AppendLine("Change rules: shown window only, delays passed on and saved, nothing changes after typing");
    }

    /// <summary>Windows only: really types a sample sequence into a window of our own and checks what arrived.</summary>
    private static async Task TypingCheck(List<string> failures, StringBuilder log)
    {
        if (!OperatingSystem.IsWindows())
        {
            log.AppendLine("Typing check: skipped (typing into windows works on Windows only)");
            return;
        }
        const string current = "Old-Sample-1!{}";
        const string fresh = "New-Sample-2?~é";
        var probe = new TypingProbeWindow();
        probe.Show();
        probe.Activate();
        await Settle();
        probe.First.Focus();
        await Settle();
        var handle = probe.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var typer = new WindowsTyper();
        var plan = TypingOrder.Plan(TypingOrder.Default, current, fresh);
        var delays = new TypingDelays(20, 200);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await typer.TypeAsync(handle, plan, delays);
        clock.Stop();
        await Settle();
        // 45 characters × 20 ms + 3 keys × 200 ms: the typing cannot be faster than the delays allow.
        var least = delays.MinimumDuration(plan);
        if (result.Ok && clock.ElapsedMilliseconds < least)
            failures.Add($"Typing check: took {clock.ElapsedMilliseconds} ms, but the delays alone need {least} ms");
        log.AppendLine($"Typing delays: 20 ms per character and 200 ms per tab took {clock.ElapsedMilliseconds} ms (at least {least} ms expected)");
        if (!result.Ok) failures.Add("Typing check: " + result.Message);
        else if (probe.First.Text != current || probe.Second.Text != fresh || probe.Third.Text != fresh || !probe.EnterPressed)
            failures.Add($"Typing check: boxes hold \"{probe.First.Text}\", \"{probe.Second.Text}\", \"{probe.Third.Text}\", Enter pressed: {probe.EnterPressed}");
        else log.AppendLine("Typing check: Current, Tab, New, Tab, New, Enter arrived exactly in a test window");
        log.AppendLine("Window used last: " + (typer.FindTarget() is null ? "none found on this machine" : "found"));
        probe.Close();
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

using Avalonia;
using PasswordGenerator.App.Services;

namespace PasswordGenerator.App;

internal static class Program
{
    // Avalonia must not be touched before BuildAvaloniaApp() runs.
    [STAThread]
    public static int Main(string[] args)
    {
        AppState.ReadArguments(args);
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            AppState.Log("Fatal: " + e);
            if (AppState.SmokeTestDir is { } dir)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "smoke-result.txt"), "FAILED\nThe app stopped while starting:\n" + e);
            }
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

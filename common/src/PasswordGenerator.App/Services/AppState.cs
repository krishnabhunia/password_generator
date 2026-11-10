using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

/// <summary>Start-up arguments and the settings held while the app runs.</summary>
public static class AppState
{
    public static string DataDir { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasswordGenerator");

    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string UpdatesDir => Path.Combine(DataDir, "updates");
    public static string LogPath => Path.Combine(DataDir, "app.log");
    /// <summary>The current password of the Change a password window, encrypted with the Windows sign-in.</summary>
    public static string CurrentPasswordPath => Path.Combine(DataDir, "current.bin");

    /// <summary>The encrypted store. Where no encryption is available (not Windows) nothing is written at all.</summary>
    public static SecretFile CurrentPasswordStore() =>
        new(CurrentPasswordPath, OperatingSystem.IsWindows() ? new DpapiProtector() : null);

    /// <summary>Folder for the self-test's pictures and result; null in normal use.</summary>
    public static string? SmokeTestDir { get; private set; }

    public static AppSettings Settings { get; set; } = new();

    public static void ReadArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--data-dir" when next is not null: DataDir = Path.GetFullPath(next); i++; break;
                case "--smoke-test" when next is not null: SmokeTestDir = Path.GetFullPath(next); i++; break;
            }
        }
    }

    public static void Load() => Settings = SettingsStore.Load(SettingsPath);

    public static void Save() => SettingsStore.Save(SettingsPath, Settings);

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(LogPath, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z {message}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never stop the app.
        }
    }
}

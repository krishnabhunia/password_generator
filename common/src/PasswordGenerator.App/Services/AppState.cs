using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

/// <summary>Start-up arguments and the settings held while the app runs.</summary>
public static class AppState
{
    /// <summary>The user's real data folder: settings and the encrypted current password.</summary>
    public static string DefaultDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasswordGenerator");

    public static string DataDir { get; private set; } = DefaultDataDir;

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
        var dataDirGiven = false;
        for (var i = 0; i < args.Length; i++)
        {
            var next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--data-dir" when next is not null: DataDir = Path.GetFullPath(next); dataDirGiven = true; i++; break;
                case "--smoke-test" when next is not null: SmokeTestDir = Path.GetFullPath(next); i++; break;
            }
        }
        // The self-test changes settings and clears the stored current password, so it must never
        // run on the user's real data folder, with or without --data-dir.
        if (SmokeTestDir is not null && (!dataDirGiven || SamePath(DataDir, DefaultDataDir)))
            DataDir = Path.Combine(SmokeTestDir, "data");
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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

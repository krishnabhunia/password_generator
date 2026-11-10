using System.Reflection;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

/// <summary>Facts about this build, written in by the pipeline.</summary>
public static class BuildInfo
{
    private static readonly Assembly Asm = typeof(BuildInfo).Assembly;

    public static string VersionText { get; } =
        (Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public static AppVersion Version { get; } = AppVersion.TryParse(VersionText, out var v) ? v : new AppVersion(0, 0, 0);

    /// <summary>A build made outside the pipeline (version 0.0.0). It never checks for updates.</summary>
    public static bool IsLocalBuild => Version == new AppVersion(0, 0, 0);

    public static string Channel => IsLocalBuild ? "Local build" : Version.IsBeta ? "Beta (test version)" : "Full release";
    public static string DisplayVersion => IsLocalBuild ? "local build" : "v" + VersionText;
    public static string SourcePr => Meta("SourcePr");
    public static string SourceCommit => Meta("SourceCommit");
    public static string BuildTimeUtc => Meta("BuildTimeUtc");
    public static string ReleaseTag => Meta("ReleaseTag");

    public static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>Installed with the set-up program (an uninstaller sits next to the app).</summary>
    public static bool IsInstalled =>
        OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public static string InstallKind => IsInstalled ? "Installed" : "Portable";

    private static string Meta(string key) =>
        Asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";

    public static string ReadDoc(string name)
    {
        using var stream = Asm.GetManifestResourceStream(name);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

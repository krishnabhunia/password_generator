using System.Text.Json;

namespace PasswordGenerator.Core;

/// <summary>What the app remembers between runs. Passwords are never part of this.</summary>
public sealed record AppSettings
{
    public const string DefaultDesign = "TwoPaneLight";
    public static readonly IReadOnlyList<string> Designs = new[] { "TwoPaneLight", "TwoPaneDark", "FluentLight", "Compact" };

    /// <summary>One of <see cref="Designs"/>. Two-pane Light is the default.</summary>
    public string Design { get; init; } = DefaultDesign;
    /// <summary>Download and install updates without asking.</summary>
    public bool AutoUpdate { get; init; }
    /// <summary>Also offer beta versions.</summary>
    public bool IncludeBeta { get; init; }
    public GeneratorOptions Options { get; init; } = new();
    /// <summary>The typing order of the Change a password window, as step names. Null means the default order.</summary>
    public IReadOnlyList<string>? TypingOrder { get; init; }

    public AppSettings Normalized() => this with
    {
        Design = Designs.Contains(Design) ? Design : DefaultDesign,
        Options = (Options ?? new GeneratorOptions()).Normalized(),
        TypingOrder = TypingOrder is null ? null : Core.TypingOrder.Names(Core.TypingOrder.Parse(TypingOrder)),
    };
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Reads settings; a missing or damaged file gives the defaults.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings()).Normalized();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes settings; returns false when the file could not be written.</summary>
    public static bool Save(string path, AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalized(), Json));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}

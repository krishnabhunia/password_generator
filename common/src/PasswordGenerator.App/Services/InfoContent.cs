using System.Runtime.InteropServices;
using System.Text;

namespace PasswordGenerator.App.Services;

public sealed record InfoRow(string Name, string Value);

/// <summary>One line of a simple document: "# heading", "## heading", "- bullet" or plain text.</summary>
public sealed record DocLine(string Text, bool IsH1, bool IsH2, bool IsBullet)
{
    public static IReadOnlyList<DocLine> Parse(string markdown)
    {
        var lines = new List<DocLine>();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var clean = line.Replace("**", "").Replace("`", "");
            if (line.StartsWith("## ")) lines.Add(new DocLine(clean[3..], false, true, false));
            else if (line.StartsWith("# ")) lines.Add(new DocLine(clean[2..], true, false, false));
            else if (line.StartsWith("- ")) lines.Add(new DocLine(clean[2..], false, false, true));
            else lines.Add(new DocLine(clean, false, false, false));
        }
        return lines;
    }
}

public static class InfoContent
{
    public static IReadOnlyList<InfoRow> BuildRows()
    {
        string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        return new[]
        {
            new InfoRow("Version", BuildInfo.VersionText),
            new InfoRow("Channel", BuildInfo.Channel),
            new InfoRow("Release tag", Or(BuildInfo.ReleaseTag, "none (local build)")),
            new InfoRow("Built (UTC)", Or(BuildInfo.BuildTimeUtc, "not recorded")),
            new InfoRow("Commit", Or(BuildInfo.SourceCommit, "not recorded")),
            new InfoRow("Pull request", string.IsNullOrWhiteSpace(BuildInfo.SourcePr) ? "none (built from main)" : "#" + BuildInfo.SourcePr),
            new InfoRow("Install type", BuildInfo.InstallKind),
            new InfoRow("Program file", BuildInfo.ExePath),
            new InfoRow("Settings file", AppState.SettingsPath),
            new InfoRow("Design", DesignInfo.Find(AppState.Settings.Design).Name),
            new InfoRow("System", RuntimeInformation.OSDescription + " · " + RuntimeInformation.OSArchitecture),
            new InfoRow("Runtime", RuntimeInformation.FrameworkDescription),
        };
    }

    public static string AsText(IEnumerable<InfoRow> rows)
    {
        var sb = new StringBuilder("Password Generator\n");
        foreach (var r in rows) sb.Append(r.Name).Append(": ").Append(r.Value).Append('\n');
        return sb.ToString();
    }

    public const string UnderTheHood = """
        # Under the hood
        ## How a password is made
        - Random numbers come from the operating system's cryptographic generator, not from a simple formula.
        - The minimum of each element is placed first, the rest is drawn from every chosen character, and the result is shuffled.
        - The strength figure is length × log2(number of possible characters), shown in bits.
        ## What is stored
        - Your choices (elements, minimums, length, design, update options) are saved in the settings file.
        - Passwords are never written to disk and never sent anywhere. Copy puts the password on the clipboard only.
        ## Updates
        - At every start the app asks GitHub Releases whether a newer version exists. That is its only network use.
        - A download is checked against its SHA-256 before it is installed.
        - The portable file replaces itself; the installed version runs the set-up program silently.
        ## Built with
        - C# on .NET 10 with the Avalonia UI toolkit, published as one self-contained file.
        - Fonts: Inter for text and JetBrains Mono for the password (both under the SIL Open Font License).
        - Source code: https://github.com/krishnabhunia/password_generator
        """;
}

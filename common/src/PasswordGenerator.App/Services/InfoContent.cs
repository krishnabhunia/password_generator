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
        - Every password that fits your choices (length, elements, minimum of each) is equally likely.
        - The strength figure is log2 of the number of different passwords your choices allow, shown in bits. Asking for many of one element lowers it.
        ## What is stored
        - Your choices (elements, minimums, length, design, update options) are saved in the settings file.
        - Generated passwords are never written to disk. Copy puts the password on the clipboard only.
        - The one exception is the current password you paste into the Change a password window. It is stored in the file current.bin next to the settings, encrypted by Windows (DPAPI) for your sign-in, so only your Windows account on this PC can read it. Forget it deletes the file.
        - No password is ever sent anywhere.
        ## Typing into another window
        - The typing uses the same Windows mechanism as KeePass auto-type: the app switches to the window you used last and sends the characters as keyboard input.
        - It stops if a different window comes to the front or you press Esc, so a password is not typed into the wrong place.
        - A window that runs as administrator does not accept typing from a normal program.
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

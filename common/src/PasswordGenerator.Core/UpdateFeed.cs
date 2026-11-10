using System.Text.Json;

namespace PasswordGenerator.Core;

public sealed record ReleaseAsset(string Name, string Url);

public sealed record ReleaseInfo(AppVersion Version, string Tag, bool Prerelease, IReadOnlyList<ReleaseAsset> Assets, string Notes)
{
    public ReleaseAsset? Asset(string name) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads the list of releases GitHub returns and chooses the one to update to.</summary>
public static class UpdateFeed
{
    public const string Owner = "krishnabhunia";
    public const string Repo = "password_generator";
    public const string ReleasesApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases?per_page=100";
    /// <summary>The newest full release, however many betas were published after it.</summary>
    public const string LatestApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Owner + "/" + Repo + "/releases";
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    public static string PortableAssetName(AppVersion v) => $"PasswordGenerator_{v}_portable.exe";
    public static string SetupAssetName(AppVersion v) => $"PasswordGenerator_{v}_setup.exe";

    /// <summary>Releases with a version we understand; drafts and unknown tags are skipped.</summary>
    /// <remarks>Accepts GitHub's release list or a single release.</remarks>
    public static IReadOnlyList<ReleaseInfo> Parse(string json)
    {
        var list = new List<ReleaseInfo>();
        using var doc = JsonDocument.Parse(json);
        // The list address returns an array, the "latest" address a single release.
        var items = doc.RootElement.ValueKind switch
        {
            JsonValueKind.Array => doc.RootElement.EnumerateArray().ToArray(),
            JsonValueKind.Object when doc.RootElement.TryGetProperty("tag_name", out _) => new[] { doc.RootElement },
            _ => Array.Empty<JsonElement>(),
        };
        foreach (var r in items)
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            if (Bool(r, "draft")) continue;
            var tag = Text(r, "tag_name");
            if (!AppVersion.TryParse(tag, out var version)) continue;
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var a in arr.EnumerateArray())
                {
                    var name = Text(a, "name");
                    var url = Text(a, "browser_download_url");
                    if (name.Length > 0 && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        assets.Add(new ReleaseAsset(name, url));
                }
            list.Add(new ReleaseInfo(version, tag, Bool(r, "prerelease") || version.IsBeta, assets, Text(r, "body")));
        }
        return list;
    }

    /// <summary>The newest release above <paramref name="current"/>, or null. Betas only when asked for.</summary>
    /// <summary>May this release be installed with the current "offer beta versions" choice?</summary>
    public static bool Allowed(ReleaseInfo release, bool includeBeta) => includeBeta || !release.Prerelease;

    public static ReleaseInfo? Pick(IEnumerable<ReleaseInfo> releases, AppVersion current, bool includeBeta) =>
        releases
            .Where(r => Allowed(r, includeBeta))
            .Where(r => r.Version.CompareTo(current) > 0)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();

    /// <summary>Finds the SHA-256 for a file in a "hash  name" list, or null.</summary>
    public static string? FindChecksum(string checksums, string fileName)
    {
        foreach (var raw in checksums.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space != 64) continue;
            var name = line[(space + 1)..].Trim().TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                return line[..64].ToLowerInvariant();
        }
        return null;
    }

    private static string Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}

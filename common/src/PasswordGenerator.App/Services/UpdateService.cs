using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

public sealed record UpdateCheck(ReleaseInfo? Update, string? Error);

/// <summary>
/// Finds new versions on GitHub Releases, downloads them with a SHA-256 check and swaps them in.
/// Portable: the running file is replaced in place. Installed: the set-up program runs silently.
/// </summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = CreateClient();
    private string? _readyFile;
    private ReleaseInfo? _readyRelease;
    private Task? _download;
    private string? _downloadTag;

    public ReleaseInfo? Found { get; private set; }
    public bool Ready => _readyFile is not null && File.Exists(_readyFile);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PasswordGenerator", BuildInfo.Version.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public async Task<UpdateCheck> CheckAsync(bool includeBeta, CancellationToken ct = default)
    {
        if (BuildInfo.IsLocalBuild) return new UpdateCheck(null, "This is a local build, so it does not check for updates.");
        if (!OperatingSystem.IsWindows()) return new UpdateCheck(null, "Updates inside the app are for Windows only.");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var releases = new List<ReleaseInfo>(UpdateFeed.Parse(await Http.GetStringAsync(UpdateFeed.ReleasesApi, cts.Token)));
            // Asked for separately, so a run of beta releases can never push the full release off the list.
            using (var latest = await Http.GetAsync(UpdateFeed.LatestApi, cts.Token))
                if (latest.IsSuccessStatusCode) // 404 simply means no full release exists yet
                    releases.AddRange(UpdateFeed.Parse(await latest.Content.ReadAsStringAsync(cts.Token)));
            Found = UpdateFeed.Pick(releases, BuildInfo.Version, includeBeta);
            DropUnless(Found);
            return new UpdateCheck(Found, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            AppState.Log("Update check failed: " + e.Message);
            return new UpdateCheck(null, "Could not reach GitHub. Check the internet connection and try again.");
        }
    }

    /// <summary>The downloaded release waiting to be installed, if any.</summary>
    public ReleaseInfo? Staged => Ready ? _readyRelease : null;

    /// <summary>
    /// Downloads the right file for this copy of the app and checks its SHA-256.
    /// A second request for the same release joins the download already running.
    /// </summary>
    public Task DownloadAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        if (Ready && _readyRelease?.Tag == release.Tag) return Task.CompletedTask;
        if (_download is { IsCompleted: false } && _downloadTag == release.Tag) return _download;
        _downloadTag = release.Tag;
        return _download = DownloadCoreAsync(release, ct);
    }

    /// <summary>Forgets a downloaded update unless it is the given release.</summary>
    public void DropUnless(ReleaseInfo? keep)
    {
        if (_readyRelease is null || _readyRelease.Tag == keep?.Tag) return;
        try { if (_readyFile is not null && File.Exists(_readyFile)) File.Delete(_readyFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* removed by CleanUp later */ }
        _readyFile = null;
        _readyRelease = null;
    }

    /// <summary>Call when "Also offer beta versions" changes: a beta found or downloaded earlier no longer counts.</summary>
    public void ApplyChannel(bool includeBeta)
    {
        if (Found is not null && !UpdateFeed.Allowed(Found, includeBeta)) Found = null;
        if (_readyRelease is not null && !UpdateFeed.Allowed(_readyRelease, includeBeta)) DropUnless(null);
    }

    private async Task DownloadCoreAsync(ReleaseInfo release, CancellationToken ct)
    {
        var name = BuildInfo.IsInstalled ? UpdateFeed.SetupAssetName(release.Version) : UpdateFeed.PortableAssetName(release.Version);
        var asset = release.Asset(name) ?? throw new InvalidOperationException($"The release {release.Tag} has no file named {name}.");
        var sums = release.Asset(UpdateFeed.ChecksumsAssetName) ?? throw new InvalidOperationException($"The release {release.Tag} has no checksum list.");

        var expected = UpdateFeed.FindChecksum(await Http.GetStringAsync(sums.Url, ct), name)
                       ?? throw new InvalidOperationException($"The checksum list has no entry for {name}.");

        Directory.CreateDirectory(AppState.UpdatesDir);
        var target = Path.Combine(AppState.UpdatesDir, name);
        var temp = target + ".download";
        await using (var source = await Http.GetStreamAsync(asset.Url, ct))
        await using (var file = File.Create(temp))
            await source.CopyToAsync(file, ct);

        string actual;
        await using (var file = File.OpenRead(temp))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temp);
            throw new InvalidOperationException("The downloaded file did not match its checksum, so it was not installed.");
        }

        File.Move(temp, target, overwrite: true);
        if (Found?.Tag != release.Tag) return; // replaced or ruled out while it was downloading
        _readyFile = target;
        _readyRelease = release;
    }

    /// <summary>Puts the downloaded version in place. Call just before the app closes.</summary>
    public void Apply(bool relaunch)
    {
        if (!Ready) throw new InvalidOperationException("No update has been downloaded.");
        var file = _readyFile!;
        if (BuildInfo.IsInstalled)
        {
            var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" + (relaunch ? " /relaunch=1" : "");
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
        }
        else
        {
            var exe = BuildInfo.ExePath;
            var old = exe + ".old";
            // Windows lets a running program be renamed, which frees its name for the new file.
            File.Move(exe, old, overwrite: true);
            try
            {
                File.Move(file, exe);
            }
            catch (Exception)
            {
                File.Move(old, exe, overwrite: true);
                throw;
            }
            if (relaunch) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        _readyFile = null;
    }

    /// <summary>Removes what a previous update left behind.</summary>
    public static void CleanUp()
    {
        try
        {
            var old = BuildInfo.ExePath + ".old";
            if (old.Length > 4 && File.Exists(old)) File.Delete(old);
            if (Directory.Exists(AppState.UpdatesDir))
                foreach (var f in Directory.GetFiles(AppState.UpdatesDir))
                    if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-2)) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still in use; it will be removed at the next start.
        }
    }
}

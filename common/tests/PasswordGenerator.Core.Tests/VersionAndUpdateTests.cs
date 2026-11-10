using PasswordGenerator.Core;
using Xunit;

namespace PasswordGenerator.Core.Tests;

public class AppVersionTests
{
    private static AppVersion V(string s)
    {
        Assert.True(AppVersion.TryParse(s, out var v), s);
        return v;
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("v15.0.0-beta007", "15.0.0-beta007")]
    [InlineData("1.0.0-beta7", "1.0.0-beta007")]
    [InlineData("1.0.0+abc123", "1.0.0")]
    public void Parses_and_prints(string text, string printed) => Assert.Equal(printed, V(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("latest")]
    [InlineData("1.2.3-rc1")]
    [InlineData("0.0.0-dev")]
    public void Rejects_other_text(string text) => Assert.False(AppVersion.TryParse(text, out _));

    [Fact]
    public void Beta_ranks_below_the_full_version()
    {
        Assert.True(V("1.0.0-beta999").CompareTo(V("1.0.0")) < 0);
        Assert.True(V("1.0.0-beta002").CompareTo(V("1.0.0-beta001")) > 0);
        Assert.True(V("1.0.1-beta001").CompareTo(V("1.0.0")) > 0);
        Assert.True(V("2.0.0").CompareTo(V("1.9.9")) > 0);
        Assert.Equal(0, V("v1.2.3").CompareTo(V("1.2.3")));
    }
}

public class UpdateFeedTests
{
    private const string Feed = """
        [
          {"tag_name":"v1.1.0-beta002","prerelease":true,"draft":false,"body":"beta","assets":[
            {"name":"PasswordGenerator_1.1.0-beta002_portable.exe","browser_download_url":"https://github.com/x/y/releases/download/v1.1.0-beta002/p.exe"}]},
          {"tag_name":"v1.0.1","prerelease":false,"draft":false,"body":"fix","assets":[
            {"name":"PasswordGenerator_1.0.1_portable.exe","browser_download_url":"https://github.com/x/y/releases/download/v1.0.1/p.exe"},
            {"name":"PasswordGenerator_1.0.1_setup.exe","browser_download_url":"https://github.com/x/y/releases/download/v1.0.1/s.exe"},
            {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/x/y/releases/download/v1.0.1/SHA256SUMS.txt"},
            {"name":"bad","browser_download_url":"http://insecure.example/file"}]},
          {"tag_name":"v2.0.0","prerelease":false,"draft":true,"assets":[]},
          {"tag_name":"nightly","prerelease":false,"draft":false,"assets":[]},
          {"tag_name":"v1.0.0","prerelease":false,"draft":false,"assets":[]}
        ]
        """;

    private static AppVersion V(string s) { AppVersion.TryParse(s, out var v); return v; }

    [Fact]
    public void Drafts_and_unknown_tags_are_skipped()
    {
        var releases = UpdateFeed.Parse(Feed);
        Assert.Equal(new[] { "v1.1.0-beta002", "v1.0.1", "v1.0.0" }, releases.Select(r => r.Tag).ToArray());
    }

    [Fact]
    public void Only_https_downloads_are_kept()
    {
        var r = UpdateFeed.Parse(Feed).Single(x => x.Tag == "v1.0.1");
        Assert.Equal(3, r.Assets.Count);
        Assert.NotNull(r.Asset(UpdateFeed.SetupAssetName(r.Version)));
        Assert.NotNull(r.Asset(UpdateFeed.PortableAssetName(r.Version)));
        Assert.Null(r.Asset("bad"));
    }

    [Fact]
    public void Full_versions_only_unless_betas_are_wanted()
    {
        var releases = UpdateFeed.Parse(Feed);
        Assert.Equal("v1.0.1", UpdateFeed.Pick(releases, V("1.0.0"), includeBeta: false)!.Tag);
        Assert.Equal("v1.1.0-beta002", UpdateFeed.Pick(releases, V("1.0.0"), includeBeta: true)!.Tag);
    }

    [Fact]
    public void Nothing_is_offered_when_up_to_date()
    {
        var releases = UpdateFeed.Parse(Feed);
        Assert.Null(UpdateFeed.Pick(releases, V("1.0.1"), includeBeta: false));
        Assert.Null(UpdateFeed.Pick(releases, V("1.1.0-beta002"), includeBeta: true));
    }

    [Fact]
    public void A_beta_user_is_offered_the_full_version()
    {
        var releases = UpdateFeed.Parse(Feed);
        Assert.Equal("v1.0.1", UpdateFeed.Pick(releases, V("1.0.1-beta004"), includeBeta: false)!.Tag);
    }

    [Fact]
    public void A_single_release_is_read_too()
    {
        // The "latest full release" address returns one release, not a list.
        var one = UpdateFeed.Parse("""{"tag_name":"v1.2.0","prerelease":false,"draft":false,"assets":[]}""");
        Assert.Equal("v1.2.0", Assert.Single(one).Tag);
    }

    [Fact]
    public void A_full_release_is_found_behind_any_number_of_betas()
    {
        // The list holds only betas (as it would after a long run of pull requests);
        // the full release comes from the separate "latest" request and is still offered.
        var betas = string.Join(",", Enumerable.Range(1, 100).Select(n =>
            $$"""{"tag_name":"v1.2.0-beta{{n:000}}","prerelease":true,"draft":false,"assets":[]}"""));
        var releases = UpdateFeed.Parse("[" + betas + "]").ToList();
        Assert.Null(UpdateFeed.Pick(releases, V("1.0.0"), includeBeta: false));
        releases.AddRange(UpdateFeed.Parse("""{"tag_name":"v1.1.0","prerelease":false,"draft":false,"assets":[]}"""));
        Assert.Equal("v1.1.0", UpdateFeed.Pick(releases, V("1.0.0"), includeBeta: false)!.Tag);
        Assert.Equal("v1.2.0-beta100", UpdateFeed.Pick(releases, V("1.0.0"), includeBeta: true)!.Tag);
    }

    [Fact]
    public void A_beta_is_not_allowed_once_betas_are_switched_off()
    {
        var releases = UpdateFeed.Parse(Feed);
        var beta = releases.Single(r => r.Tag == "v1.1.0-beta002");
        var full = releases.Single(r => r.Tag == "v1.0.1");
        Assert.True(UpdateFeed.Allowed(beta, includeBeta: true));
        Assert.False(UpdateFeed.Allowed(beta, includeBeta: false));
        Assert.True(UpdateFeed.Allowed(full, includeBeta: false));
    }

    [Fact]
    public void Feed_that_is_not_a_list_gives_no_releases() => Assert.Empty(UpdateFeed.Parse("""{"message":"rate limited"}"""));

    [Fact]
    public void Checksum_is_found_by_file_name()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = $"{a}  one.exe\r\n{b} *Two.exe\nshort  three.exe\n";
        Assert.Equal(a, UpdateFeed.FindChecksum(sums, "one.exe"));
        Assert.Equal(b.ToLowerInvariant(), UpdateFeed.FindChecksum(sums, "two.exe"));
        Assert.Null(UpdateFeed.FindChecksum(sums, "three.exe"));
        Assert.Null(UpdateFeed.FindChecksum(sums, "missing.exe"));
    }
}

public class SettingsStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "pwgen-tests-" + Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Two_pane_light_is_the_default_design()
    {
        Assert.Equal("TwoPaneLight", new AppSettings().Design);
        Assert.Equal(new[] { "TwoPaneLight", "TwoPaneDark", "FluentLight", "Compact" }, AppSettings.Designs.ToArray());
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = SettingsStore.Load(TempFile());
        Assert.Equal(new AppSettings().Design, s.Design);
        Assert.False(s.AutoUpdate);
        Assert.False(s.IncludeBeta);
        Assert.Equal(16, s.Options.Length);
    }

    [Fact]
    public void Saved_settings_come_back()
    {
        var path = TempFile();
        var saved = new AppSettings
        {
            Design = "Compact", AutoUpdate = true, IncludeBeta = true,
            Options = new GeneratorOptions { Length = 24, Digits = false, MinUpper = 3, Symbols = "#$" },
        };
        Assert.True(SettingsStore.Save(path, saved));
        var s = SettingsStore.Load(path);
        Assert.Equal(saved, s);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Damaged_file_gives_defaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(new AppSettings(), SettingsStore.Load(path));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Out_of_range_values_are_corrected()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"Design":"Z","Options":{"Length":900,"MinUpper":50,"Symbols":"abc#"}}""");
        var s = SettingsStore.Load(path);
        Assert.Equal("TwoPaneLight", s.Design);
        Assert.Equal(64, s.Options.Length);
        Assert.Equal(9, s.Options.MinUpper);
        Assert.Equal("#", s.Options.Symbols);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}

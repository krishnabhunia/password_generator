using System.Text;
using PasswordGenerator.Core;
using Xunit;

namespace PasswordGenerator.Core.Tests;

public class TypingOrderTests
{
    [Fact]
    public void Default_order_fits_old_new_confirm()
    {
        Assert.Equal("Current, Tab, New, Tab, New, Enter", TypingOrder.Describe(TypingOrder.Default));
    }

    [Fact]
    public void Plan_types_each_step_in_order()
    {
        var steps = new[] { TypingStep.Current, TypingStep.Tab, TypingStep.New, TypingStep.Wait, TypingStep.New, TypingStep.Enter };
        var plan = TypingOrder.Plan(steps, "old#1", "new#2");
        Assert.Equal(new TypingAction[]
        {
            new TypingAction.Text("old#1"), new TypingAction.Key(TypingKey.Tab), new TypingAction.Text("new#2"),
            new TypingAction.Pause(1000), new TypingAction.Text("new#2"), new TypingAction.Key(TypingKey.Enter),
        }, plan);
    }

    [Fact]
    public void Order_without_current_does_not_need_one()
    {
        var steps = new[] { TypingStep.New, TypingStep.Tab, TypingStep.New };
        Assert.Null(TypingOrder.Problem(steps, "", "new#2"));
        Assert.Equal(3, TypingOrder.Plan(steps, "", "new#2").Count);
    }

    [Fact]
    public void Problems_are_reported_before_typing()
    {
        Assert.Contains("current password", TypingOrder.Problem(TypingOrder.Default, "", "new")!);
        Assert.Contains("no new password", TypingOrder.Problem(TypingOrder.Default, "old", "")!);
        Assert.Contains("Add Current or New", TypingOrder.Problem(new[] { TypingStep.Tab, TypingStep.Enter }, "old", "new")!);
        Assert.Contains("Add Current or New", TypingOrder.Problem(Array.Empty<TypingStep>(), "old", "new")!);
        Assert.Throws<InvalidOperationException>(() => TypingOrder.Plan(TypingOrder.Default, "", "new"));
    }

    [Fact]
    public void Saved_order_is_read_back_and_cleaned()
    {
        Assert.Equal(TypingOrder.Default, TypingOrder.Parse(null));
        Assert.Equal(new[] { TypingStep.New, TypingStep.Enter }, TypingOrder.Parse(new[] { "new", "Bogus", "ENTER", "99" }));
        Assert.Empty(TypingOrder.Parse(Array.Empty<string>()));
        Assert.Equal(TypingOrder.MaxSteps, TypingOrder.Parse(Enumerable.Repeat("Tab", 50)).Count);
        Assert.Equal(new[] { "Current", "Tab", "New", "Tab", "New", "Enter" }, TypingOrder.Names(TypingOrder.Default));
    }

    [Fact]
    public void Order_is_kept_in_the_settings_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "pwgen-tests-" + Guid.NewGuid().ToString("N"), "settings.json");
        Assert.Null(SettingsStore.Load(path).TypingOrder);
        SettingsStore.Save(path, new AppSettings { TypingOrder = new[] { "New", "Tab", "New", "nonsense" } });
        Assert.Equal(new[] { "New", "Tab", "New" }, SettingsStore.Load(path).TypingOrder);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}

public class TypingDelayTests
{
    [Fact]
    public void Defaults_are_10_and_100_milliseconds()
    {
        var d = new TypingDelays();
        Assert.Equal((10, 100), (d.CharMs, d.KeyMs));
        Assert.Equal(d, new AppSettings().Delays());
    }

    [Fact]
    public void Delays_are_kept_inside_their_limits()
    {
        Assert.Equal(new TypingDelays(0, 0), new TypingDelays(-5, -1).Normalized());
        Assert.Equal(new TypingDelays(500, 5000), new TypingDelays(9999, 99999).Normalized());
        Assert.Equal(new TypingDelays(35, 250), new TypingDelays(35, 250).Normalized());
    }

    [Fact]
    public void Shortest_duration_counts_every_character_key_and_wait()
    {
        var steps = new[] { TypingStep.Current, TypingStep.Tab, TypingStep.New, TypingStep.Wait, TypingStep.Enter };
        var plan = TypingOrder.Plan(steps, "abcd", "123456");
        // 10 characters × 20 ms + 2 keys × 300 ms + one 1000 ms wait.
        Assert.Equal(10 * 20 + 2 * 300 + 1000, new TypingDelays(20, 300).MinimumDuration(plan));
        Assert.Equal(1000, new TypingDelays(0, 0).MinimumDuration(plan));
    }

    [Fact]
    public void Delays_are_kept_in_the_settings_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "pwgen-tests-" + Guid.NewGuid().ToString("N"), "settings.json");
        SettingsStore.Save(path, new AppSettings { CharDelayMs = 45, TabDelayMs = 650 });
        var back = SettingsStore.Load(path);
        Assert.Equal(new TypingDelays(45, 650), back.Delays());

        File.WriteAllText(path, """{"CharDelayMs":-20,"TabDelayMs":999999}""");
        Assert.Equal(new TypingDelays(0, 5000), SettingsStore.Load(path).Delays());

        // A settings file from version 1.1.0 has no delays: the defaults apply.
        File.WriteAllText(path, """{"Design":"Compact"}""");
        Assert.Equal(new TypingDelays(), SettingsStore.Load(path).Delays());
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}

public class SecretFileTests
{
    /// <summary>Stands in for Windows data protection: reversible, and unreadable as plain text.</summary>
    private sealed class FlipProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0xA5)).Reverse().ToArray();
        public byte[] Unprotect(byte[] sealedBytes) => sealedBytes.Reverse().Select(b => (byte)(b ^ 0xA5)).ToArray();
    }

    private sealed class BrokenProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => throw new System.Security.Cryptography.CryptographicException("no key");
        public byte[] Unprotect(byte[] sealedBytes) => throw new System.Security.Cryptography.CryptographicException("no key");
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "pwgen-tests-" + Guid.NewGuid().ToString("N"), "current.bin");

    [Fact]
    public void Secret_comes_back_after_saving()
    {
        var path = TempFile();
        var file = new SecretFile(path, new FlipProtector());
        Assert.True(file.CanStore);
        Assert.True(file.Save("Old-Passw0rd! ü€"));
        Assert.Equal("Old-Passw0rd! ü€", new SecretFile(path, new FlipProtector()).Load());
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void The_file_never_holds_the_password_in_the_clear()
    {
        var path = TempFile();
        new SecretFile(path, new FlipProtector()).Save("Old-Passw0rd!");
        var raw = File.ReadAllBytes(path);
        Assert.DoesNotContain("Old-Passw0rd!", Encoding.UTF8.GetString(raw));
        Assert.DoesNotContain("Old-Passw0rd!", Encoding.Unicode.GetString(raw));
        Assert.Equal("PWG1", Encoding.ASCII.GetString(raw, 0, 4));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Empty_secret_or_forget_removes_the_file()
    {
        var path = TempFile();
        var file = new SecretFile(path, new FlipProtector());
        file.Save("something");
        Assert.True(File.Exists(path));
        Assert.True(file.Save(""));
        Assert.False(File.Exists(path));
        file.Save("again");
        file.Delete();
        Assert.False(File.Exists(path));
        Assert.Equal("", file.Load());
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Without_protection_nothing_is_written()
    {
        var path = TempFile();
        var file = new SecretFile(path, null);
        Assert.False(file.CanStore);
        Assert.False(file.Save("Old-Passw0rd!"));
        Assert.False(File.Exists(path));
        Assert.Equal("", file.Load());
    }

    [Fact]
    public void Damaged_or_foreign_file_reads_as_empty()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.Equal("", new SecretFile(path, new FlipProtector()).Load());
        File.WriteAllText(path, "PWG1");
        Assert.Equal("", new SecretFile(path, new FlipProtector()).Load());
        new SecretFile(path, new FlipProtector()).Save("real");
        Assert.Equal("", new SecretFile(path, new BrokenProtector()).Load()); // another user or PC cannot decrypt
        Assert.False(new SecretFile(path, new BrokenProtector()).Save("x"));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}

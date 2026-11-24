using System.Text.Json;
using PasswordGenerator.Core;
using Xunit;

namespace PasswordGenerator.Core.Tests;

public class ClipboardTests
{
    private sealed class FakeClipboard : IPasswordClipboard
    {
        public string? Text { get; set; }
        public int Clears { get; private set; }
        public bool KeepsOutOfHistory => true;
        public Task SetAsync(string text) { Text = text; return Task.CompletedTask; }
        public Task<string?> GetAsync() => Task.FromResult(Text);
        public Task ClearAsync() { Text = null; Clears++; return Task.CompletedTask; }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(25, 30)]
    [InlineData(20, 10)]
    [InlineData(-5, 0)]
    [InlineData(1000, 60)]
    [InlineData(int.MinValue, 0)]
    [InlineData(int.MaxValue, 60)]
    public void Clear_time_is_one_of_the_choices(int stored, int expected)
    {
        Assert.Equal(expected, ClipboardClear.Normalize(stored));
    }

    [Fact]
    public void Labels_and_notes_read_as_designed()
    {
        Assert.Equal(new[] { "Off", "10 s", "30 s", "60 s" }, ClipboardClear.Choices.Select(ClipboardClear.Label));
        Assert.Equal("Copied. Clears from the clipboard in 30 s · not saved to Win + V history", ClipboardClear.CopiedNote(30, true));
        Assert.Equal("Copied · not saved to Win + V history", ClipboardClear.CopiedNote(0, true));
        Assert.Equal("Copied. Clears from the clipboard in 10 s.", ClipboardClear.CopiedNote(10, false));
    }

    [Fact]
    public void Settings_default_to_30_seconds_and_keep_a_choice()
    {
        Assert.Equal(30, new AppSettings().ClipboardClearSeconds);
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Design\":\"TwoPaneLight\"}")!.Normalized();
        Assert.Equal(30, old.ClipboardClearSeconds);
        Assert.Equal(0, (new AppSettings() with { ClipboardClearSeconds = 0 }).Normalized().ClipboardClearSeconds);
        Assert.Equal(60, (new AppSettings() with { ClipboardClearSeconds = 99 }).Normalized().ClipboardClearSeconds);
    }

    [Fact]
    public async Task Copied_password_is_cleared_after_the_time()
    {
        var clipboard = new FakeClipboard();
        var guard = new ClipboardGuard(clipboard);
        var cleared = 0;
        guard.Cleared += () => cleared++;

        await guard.CopyAsync("Sample-1!", TimeSpan.FromMilliseconds(50));
        Assert.Equal("Sample-1!", clipboard.Text);
        await guard.PendingClear!;

        Assert.Null(clipboard.Text);
        Assert.Equal(1, cleared);
    }

    [Fact]
    public async Task Something_copied_later_is_left_alone()
    {
        var clipboard = new FakeClipboard();
        var guard = new ClipboardGuard(clipboard);
        await guard.CopyAsync("Sample-1!", TimeSpan.FromMilliseconds(50));
        var pending = guard.PendingClear!;
        clipboard.Text = "a note the user copied";
        await pending;

        Assert.Equal("a note the user copied", clipboard.Text);
        Assert.Equal(0, clipboard.Clears);
    }

    [Fact]
    public async Task A_second_copy_replaces_the_first_clearing()
    {
        var clipboard = new FakeClipboard();
        var guard = new ClipboardGuard(clipboard);
        await guard.CopyAsync("First-1!", TimeSpan.FromMilliseconds(30));
        var first = guard.PendingClear!;
        await guard.CopyAsync("Second-2!", TimeSpan.FromSeconds(30));
        await first;

        Assert.Equal("Second-2!", clipboard.Text);
        Assert.Equal(0, clipboard.Clears);
        Assert.NotNull(guard.PendingClear);
        guard.Cancel();
    }

    [Fact]
    public async Task Off_plans_no_clearing()
    {
        var clipboard = new FakeClipboard();
        var guard = new ClipboardGuard(clipboard);
        await guard.CopyAsync("Sample-1!", TimeSpan.Zero);

        Assert.Equal("Sample-1!", clipboard.Text);
        Assert.Null(guard.PendingClear);
        await guard.ClearNowIfPendingAsync();
        Assert.Equal("Sample-1!", clipboard.Text);
    }

    [Fact]
    public async Task Closing_the_app_clears_a_waiting_password_at_once()
    {
        var clipboard = new FakeClipboard();
        var guard = new ClipboardGuard(clipboard);
        await guard.CopyAsync("Sample-1!", TimeSpan.FromMinutes(1));
        await guard.ClearNowIfPendingAsync();

        Assert.Null(clipboard.Text);
        Assert.Null(guard.PendingClear);
        Assert.Equal(1, clipboard.Clears);
    }
}

namespace PasswordGenerator.Core;

/// <summary>How long a copied new password may stay on the clipboard (a Settings choice).</summary>
public static class ClipboardClear
{
    public const int DefaultSeconds = 30;

    /// <summary>The Settings choices, in seconds. 0 means Off: the password stays until something else is copied.</summary>
    public static readonly IReadOnlyList<int> Choices = new[] { 0, 10, 30, 60 };

    /// <summary>The nearest of <see cref="Choices"/>; a damaged settings file can hold anything.</summary>
    public static int Normalize(int seconds) =>
        Choices.Contains(seconds) ? seconds : Choices.OrderBy(c => Math.Abs((long)c - seconds)).ThenBy(c => c).First();

    public static string Label(int seconds) => seconds <= 0 ? "Off" : seconds + " s";

    /// <summary>The line shown under the new password after Copy.</summary>
    public static string CopiedNote(int seconds, bool keptOutOfHistory)
    {
        var note = seconds > 0 ? $"Copied. Clears from the clipboard in {seconds} s" : "Copied";
        return keptOutOfHistory ? note + " · not saved to Win + V history" : note + ".";
    }

    public const string ClearedNote = "Cleared from the clipboard.";
    public const string BusyNote = "The clipboard is busy. Press Copy again.";
}

/// <summary>The clipboard as the Copy button of the Change a password window uses it.</summary>
public interface IPasswordClipboard
{
    /// <summary>True when copied text is kept out of clipboard history and cloud clipboard (Win + V on Windows).</summary>
    bool KeepsOutOfHistory { get; }
    Task SetAsync(string text);
    Task<string?> GetAsync();
    Task ClearAsync();
}

/// <summary>
/// Copies a password and clears it from the clipboard later, but only if the clipboard still holds it:
/// anything copied since then is left alone. A second copy replaces the first one's clearing. When the clipboard
/// is busy at clearing time the guard tries again, and it keeps the password in mind until a clearing worked,
/// so closing the app still clears it.
/// </summary>
public sealed class ClipboardGuard
{
    /// <summary>How often a busy clipboard is tried again after the clearing time (30 tries, one a second by default).</summary>
    public const int MaxRetries = 30;

    private readonly IPasswordClipboard _clipboard;
    private readonly TimeSpan _retryDelay;
    private CancellationTokenSource? _pending;
    private string? _pendingText;

    public ClipboardGuard(IPasswordClipboard clipboard, TimeSpan? retryDelay = null)
    {
        _clipboard = clipboard;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
    }

    public bool KeepsOutOfHistory => _clipboard.KeepsOutOfHistory;

    /// <summary>Raised after the guard cleared the clipboard.</summary>
    public event Action? Cleared;

    /// <summary>The clearing that is waiting, for tests; null when nothing waits.</summary>
    public Task? PendingClear { get; private set; }

    /// <summary>True while a copied password is still to be cleared.</summary>
    public bool HasPending => _pendingText is not null;

    /// <summary>
    /// Puts the text on the clipboard and, when <paramref name="clearAfter"/> is above zero, clears it after that time.
    /// If the clipboard cannot be written, the exception comes through and an earlier copy's clearing stays as it was.
    /// </summary>
    public async Task CopyAsync(string text, TimeSpan clearAfter)
    {
        await _clipboard.SetAsync(text);
        // The earlier password is no longer on the clipboard, so its clearing can go.
        Cancel();
        if (clearAfter <= TimeSpan.Zero) return;
        var cts = new CancellationTokenSource();
        _pending = cts;
        _pendingText = text;
        PendingClear = ClearLaterAsync(text, clearAfter, cts);
    }

    /// <summary>Clears at once if a copied password is still waiting to be cleared (used when the app closes).</summary>
    public async Task ClearNowIfPendingAsync()
    {
        if (_pendingText is not { } text) return;
        var cts = _pending;
        if (await TryClearAsync(text) && ReferenceEquals(_pending, cts)) Cancel();
    }

    /// <summary>Forgets the waiting clearing without touching the clipboard.</summary>
    public void Cancel()
    {
        _pending?.Cancel();
        _pending = null;
        _pendingText = null;
        PendingClear = null;
    }

    private async Task ClearLaterAsync(string text, TimeSpan delay, CancellationTokenSource cts)
    {
        var wait = delay;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await Task.Delay(wait, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (cts.IsCancellationRequested || !ReferenceEquals(_pending, cts)) return;
            if (await TryClearAsync(text))
            {
                if (ReferenceEquals(_pending, cts))
                {
                    _pending = null;
                    _pendingText = null;
                }
                return;
            }
            // Busy every time: the password stays in mind, so closing the app still clears it.
            if (attempt >= MaxRetries) return;
            wait = _retryDelay;
        }
    }

    /// <summary>True when the clipboard no longer holds the text (cleared now, or something else was copied); false when it was busy.</summary>
    private async Task<bool> TryClearAsync(string text)
    {
        try
        {
            if (await _clipboard.GetAsync() != text) return true; // something else was copied since: leave it
            await _clipboard.ClearAsync();
            Cleared?.Invoke();
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return false; // the clipboard is busy for a moment
        }
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Input.Platform;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

public static class PasswordClipboard
{
    /// <summary>The Windows clipboard with the history opt-outs where possible, Avalonia's clipboard elsewhere.</summary>
    public static IPasswordClipboard Create(Func<IntPtr> owner, Func<IClipboard?> fallback) =>
        OperatingSystem.IsWindows() ? new WindowsPasswordClipboard(owner) : new AvaloniaPasswordClipboard(fallback);
}

/// <summary>Avalonia's clipboard, for systems other than Windows. Nothing is kept out of history there.</summary>
public sealed class AvaloniaPasswordClipboard : IPasswordClipboard
{
    private readonly Func<IClipboard?> _clipboard;

    public AvaloniaPasswordClipboard(Func<IClipboard?> clipboard) => _clipboard = clipboard;

    public bool KeepsOutOfHistory => false;

    public async Task SetAsync(string text)
    {
        if (_clipboard() is not { } clipboard) throw new IOException("No clipboard is available.");
        try { await clipboard.SetTextAsync(text); }
        catch (Exception e) when (e is not IOException) { throw new IOException("The clipboard is busy.", e); }
    }

    public async Task<string?> GetAsync()
    {
        if (_clipboard() is not { } clipboard) return null;
        try { return await clipboard.TryGetTextAsync(); }
        catch (Exception e) when (e is not IOException) { throw new IOException("The clipboard is busy.", e); }
    }

    public async Task ClearAsync()
    {
        if (_clipboard() is not { } clipboard) return;
        try { await clipboard.ClearAsync(); }
        catch (Exception e) when (e is not IOException) { throw new IOException("The clipboard is busy.", e); }
    }
}

/// <summary>
/// The Windows clipboard, written directly so the copied password can carry the formats that keep it out of
/// Win + V clipboard history, out of cloud clipboard sync and away from clipboard monitors (as KeePass does).
/// Every call finishes before it returns, so it also works while the app is closing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPasswordClipboard : IPasswordClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>Registered formats that tell Windows and clipboard tools not to keep or sync this content.</summary>
    public static readonly IReadOnlyList<string> PrivacyFormats = new[]
    {
        "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
    };

    private readonly Func<IntPtr> _owner;

    /// <param name="owner">A window of this app; Windows needs one to accept new clipboard content.</param>
    public WindowsPasswordClipboard(Func<IntPtr> owner) => _owner = owner;

    public bool KeepsOutOfHistory => true;

    public Task SetAsync(string text)
    {
        Open(_owner());
        try
        {
            if (!EmptyClipboard()) throw Fail("could not be emptied");
            Put(CfUnicodeText, System.Text.Encoding.Unicode.GetBytes(text + "\0"));
            // A DWORD 0 means "no" for history and cloud upload; the monitor format only has to be present.
            var no = new byte[4];
            foreach (var name in PrivacyFormats) Put(Register(name), no);
        }
        finally
        {
            CloseClipboard();
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync()
    {
        if (!IsClipboardFormatAvailable(CfUnicodeText)) return Task.FromResult<string?>(null);
        Open(IntPtr.Zero);
        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero) return Task.FromResult<string?>(null);
            var data = GlobalLock(handle);
            if (data == IntPtr.Zero) return Task.FromResult<string?>(null);
            try { return Task.FromResult(Marshal.PtrToStringUni(data)); }
            finally { GlobalUnlock(handle); }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public Task ClearAsync()
    {
        Open(IntPtr.Zero);
        try
        {
            if (!EmptyClipboard()) throw Fail("could not be emptied");
        }
        finally
        {
            CloseClipboard();
        }
        return Task.CompletedTask;
    }

    /// <summary>True when the named format is on the clipboard now (for the self-test).</summary>
    public static bool HasFormat(string name) => IsClipboardFormatAvailable(Register(name));

    private static uint Register(string name)
    {
        var id = RegisterClipboardFormatW(name);
        return id != 0 ? id : throw Fail("format " + name + " could not be registered");
    }

    private static void Open(IntPtr owner)
    {
        // Another program may hold the clipboard for a moment.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner)) return;
            Thread.Sleep(20);
        }
        throw new IOException("The clipboard is busy.");
    }

    private static void Put(uint format, byte[] bytes)
    {
        var memory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) throw Fail("memory could not be allocated");
        var target = GlobalLock(memory);
        if (target == IntPtr.Zero)
        {
            GlobalFree(memory);
            throw Fail("memory could not be locked");
        }
        Marshal.Copy(bytes, 0, target, bytes.Length);
        GlobalUnlock(memory);
        // After a successful SetClipboardData the memory belongs to Windows.
        if (SetClipboardData(format, memory) == IntPtr.Zero)
        {
            GlobalFree(memory);
            throw Fail("content could not be set");
        }
    }

    private static IOException Fail(string what) => new($"Clipboard: {what} (error {Marshal.GetLastWin32Error()}).");

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr memory);
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

public sealed record TargetWindow(IntPtr Handle, string Title);

public sealed record TypeResult(bool Ok, string Message);

/// <summary>Types text and keys into another window, the way KeePass auto-type does.</summary>
public interface IKeyboardTyper
{
    /// <summary>False where typing into other windows is not supported (anything but Windows).</summary>
    bool Available { get; }

    /// <summary>The window used just before this app: the top-most visible window of another program.</summary>
    TargetWindow? FindTarget();

    /// <summary>Brings the window to the front and types the plan into it.</summary>
    Task<TypeResult> TypeAsync(IntPtr target, IReadOnlyList<TypingAction> plan, CancellationToken ct = default);
}

public sealed class NoTyper : IKeyboardTyper
{
    public bool Available => false;
    public TargetWindow? FindTarget() => null;
    public Task<TypeResult> TypeAsync(IntPtr target, IReadOnlyList<TypingAction> plan, CancellationToken ct = default) =>
        Task.FromResult(new TypeResult(false, "Typing into other windows works on Windows only."));
}

public static class KeyboardTyper
{
    public static IKeyboardTyper Create() => OperatingSystem.IsWindows() ? new WindowsTyper() : new NoTyper();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsTyper : IKeyboardTyper
{
    private const int CharDelayMs = 8;
    private const int KeyDelayMs = 60;
    private const string Partly = " Part of the sequence may already have been typed.";

    public bool Available => true;

    public TargetWindow? FindTarget()
    {
        var own = (uint)Environment.ProcessId;
        // Top-level windows come back in front-to-back order, so the first match is the one used last.
        for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, GwHwndNext))
        {
            if (!IsWindowVisible(h) || IsIconic(h)) continue;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == own) continue;
            if ((GetWindowLongPtr(h, GwlExStyle).ToInt64() & WsExToolWindow) != 0) continue;
            if (DwmGetWindowAttribute(h, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            var title = Text(h, title: true);
            if (title.Length == 0) continue;
            if (Text(h, title: false) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") continue;
            return new TargetWindow(h, title);
        }
        return null;
    }

    public Task<TypeResult> TypeAsync(IntPtr target, IReadOnlyList<TypingAction> plan, CancellationToken ct = default) =>
        Task.Run(() => Run(target, plan, ct), CancellationToken.None);

    private static TypeResult Run(IntPtr target, IReadOnlyList<TypingAction> plan, CancellationToken ct)
    {
        if (!IsWindow(target)) return new TypeResult(false, "That window is no longer open. Nothing was typed.");

        // A key still held from pressing the button (Enter, Space, Ctrl) would mix into the typing.
        WaitUntil(() => !AnyDown(VkShift, VkControl, VkMenu, VkLWin, VkRWin, VkReturn, VkSpace), 1500);
        SetForegroundWindow(target);
        if (!WaitUntil(() => GetForegroundWindow() == target, 700))
        {
            // Windows sometimes refuses a switch it did not see the user ask for. A tap on Alt lifts that block.
            SendVirtualKey(VkMenu);
            SetForegroundWindow(target);
            if (!WaitUntil(() => GetForegroundWindow() == target, 1200))
                return new TypeResult(false, "Windows did not switch to the other window. Nothing was typed.");
        }
        Thread.Sleep(250);

        var first = true;
        foreach (var action in plan)
        {
            switch (action)
            {
                case TypingAction.Text text:
                    foreach (var c in text.Value)
                    {
                        if (StopReason(target, ct) is { } why) return new TypeResult(false, why + (first ? " Nothing was typed." : Partly));
                        if (!SendUnicode(c)) return new TypeResult(false, Refused + (first ? " Nothing was typed." : Partly));
                        first = false;
                        Thread.Sleep(CharDelayMs);
                    }
                    break;
                case TypingAction.Key key:
                    if (StopReason(target, ct) is { } stop) return new TypeResult(false, stop + (first ? " Nothing was typed." : Partly));
                    if (!SendVirtualKey(key.Which == TypingKey.Tab ? VkTab : VkReturn)) return new TypeResult(false, Refused + (first ? " Nothing was typed." : Partly));
                    first = false;
                    Thread.Sleep(KeyDelayMs);
                    break;
                case TypingAction.Pause pause:
                    for (var waited = 0; waited < pause.Milliseconds; waited += 50)
                    {
                        if (ct.IsCancellationRequested || Down(VkEscape)) break;
                        Thread.Sleep(50);
                    }
                    break;
            }
        }
        return new TypeResult(true, "");
    }

    private const string Refused = "Windows did not accept the typing. The other window may be running as administrator.";

    /// <summary>Why typing must stop now, or null to carry on.</summary>
    private static string? StopReason(IntPtr target, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return "Cancelled.";
        if (Down(VkEscape)) return "Stopped with Esc.";
        // Never type a password into a window other than the one that was shown to the user.
        if (GetForegroundWindow() != target) return "Stopped: another window came to the front.";
        return null;
    }

    private static bool SendUnicode(char c)
    {
        var inputs = new[]
        {
            new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeybdInput { Scan = c, Flags = KeyeventfUnicode } } },
            new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeybdInput { Scan = c, Flags = KeyeventfUnicode | KeyeventfKeyUp } } },
        };
        return SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2;
    }

    private static bool SendVirtualKey(ushort vk)
    {
        var inputs = new[]
        {
            new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeybdInput { Vk = vk } } },
            new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeybdInput { Vk = vk, Flags = KeyeventfKeyUp } } },
        };
        return SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2;
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool AnyDown(params int[] keys) => keys.Any(Down);

    private static bool WaitUntil(Func<bool> done, int milliseconds)
    {
        for (var waited = 0; waited < milliseconds; waited += 25)
        {
            if (done()) return true;
            Thread.Sleep(25);
        }
        return done();
    }

    private static string Text(IntPtr h, bool title)
    {
        var buffer = new StringBuilder(260);
        var length = title ? GetWindowText(h, buffer, buffer.Capacity) : GetClassName(h, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : "";
    }

    // ---- Win32 ----
    private const uint GwHwndNext = 2;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const int DwmwaCloaked = 14;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyUp = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkMenu = 0x12;
    private const int VkShift = 0x10, VkControl = 0x11, VkEscape = 0x1B, VkSpace = 0x20, VkLWin = 0x5B, VkRWin = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mi;
        [FieldOffset(0)] public KeybdInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
}

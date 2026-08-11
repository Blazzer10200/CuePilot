using System.Runtime.InteropServices;
using System.Text;

namespace WorkflowLooper;

internal static class NativeMethods
{
    internal const int WhKeyboardLl = 13;
    internal const int WhMouseLl = 14;
    internal const int HcAction = 0;
    internal const int WmHotkey = 0x0312;
    internal const int WmKeydown = 0x0100;
    internal const int WmKeyup = 0x0101;
    internal const int WmSyskeydown = 0x0104;
    internal const int WmSyskeyup = 0x0105;
    internal const int WmMousemove = 0x0200;
    internal const int WmLbuttondown = 0x0201;
    internal const int WmLbuttonup = 0x0202;
    internal const int WmRbuttondown = 0x0204;
    internal const int WmRbuttonup = 0x0205;
    internal const int WmMbuttondown = 0x0207;
    internal const int WmMbuttonup = 0x0208;
    internal const int WmMousewheel = 0x020A;
    internal const int WmXbuttondown = 0x020B;
    internal const int WmXbuttonup = 0x020C;
    internal const int WmMousehwheel = 0x020E;

    internal const uint LlkhfExtended = 0x01;
    internal const uint LlkhfInjected = 0x10;
    internal const uint LlmhfInjected = 0x01;

    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModNoRepeat = 0x4000;

    internal const uint InputMouse = 0;
    internal const uint InputKeyboard = 1;
    internal const uint KeyeventfExtendedkey = 0x0001;
    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint KeyeventfScancode = 0x0008;
    internal const uint MouseeventfMove = 0x0001;
    internal const uint MouseeventfLeftdown = 0x0002;
    internal const uint MouseeventfLeftup = 0x0004;
    internal const uint MouseeventfRightdown = 0x0008;
    internal const uint MouseeventfRightup = 0x0010;
    internal const uint MouseeventfMiddledown = 0x0020;
    internal const uint MouseeventfMiddleup = 0x0040;
    internal const uint MouseeventfXdown = 0x0080;
    internal const uint MouseeventfXup = 0x0100;
    internal const uint MouseeventfWheel = 0x0800;
    internal const uint MouseeventfHwheel = 0x1000;
    internal const uint MouseeventfVirtualdesk = 0x4000;
    internal const uint MouseeventfAbsolute = 0x8000;
    internal const uint CreateWaitableTimerHighResolution = 0x00000002;
    internal const uint TimerModifyState = 0x0002;
    internal const uint Synchronize = 0x00100000;
    internal const uint WaitObject0 = 0x00000000;
    internal const uint WaitFailed = 0xffffffff;

    internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHookData
    {
        internal Point Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookData
    {
        internal uint VirtualKey;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] internal MouseInput Mouse;
        [FieldOffset(0)] internal KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, ref Input input, int size);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string? timerName, uint flags, uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWaitableTimerEx(
        IntPtr timer,
        ref long dueTime,
        int period,
        IntPtr completionRoutine,
        IntPtr completionArgument,
        IntPtr wakeContext,
        uint tolerableDelay);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    internal static extern int SetWindowTheme(IntPtr window, string? subApplicationName, string? subIdentifierList);

    internal static int InputSize => Marshal.SizeOf<Input>();

    internal static int SignedHighWord(uint value) => unchecked((short)((value >> 16) & 0xffff));

    internal static uint HighWord(uint value) => (value >> 16) & 0xffff;
}

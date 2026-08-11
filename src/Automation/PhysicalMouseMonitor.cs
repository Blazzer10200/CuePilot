using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WorkflowLooper;

internal sealed class PhysicalMouseMonitor : IDisposable
{
    private readonly NativeMethods.HookProc callback;
    private IntPtr hook;

    internal event EventHandler<PhysicalMouseEventArgs>? LeftButtonChanged;

    internal PhysicalMouseMonitor() => callback = MouseHook;

    internal void Start()
    {
        if (hook != IntPtr.Zero)
        {
            return;
        }

        hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, callback, NativeMethods.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not monitor physical mouse input.");
        }
    }

    internal void Stop()
    {
        if (hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == NativeMethods.HcAction)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);
            if ((data.Flags & NativeMethods.LlmhfInjected) == 0)
            {
                var message = wParam.ToInt32();
                if (message is NativeMethods.WmLbuttondown or NativeMethods.WmLbuttonup)
                {
                    LeftButtonChanged?.Invoke(this, new PhysicalMouseEventArgs(
                        message == NativeMethods.WmLbuttondown,
                        Stopwatch.GetTimestamp(),
                        new Point(data.Point.X, data.Point.Y)));
                }
            }
        }

        return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose() => Stop();
}

internal sealed class PhysicalMouseEventArgs(bool isDown, long timestamp, Point position) : EventArgs
{
    internal bool IsDown { get; } = isDown;
    internal long Timestamp { get; } = timestamp;
    internal Point Position { get; } = position;
}

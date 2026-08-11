using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WorkflowLooper;

internal sealed class PrecisionWaiter : IDisposable
{
    private IntPtr timer;

    internal bool IsHighResolution { get; }

    internal PrecisionWaiter()
    {
        var access = NativeMethods.TimerModifyState | NativeMethods.Synchronize;
        timer = NativeMethods.CreateWaitableTimerEx(
            IntPtr.Zero,
            null,
            NativeMethods.CreateWaitableTimerHighResolution,
            access);
        IsHighResolution = timer != IntPtr.Zero;
        if (timer == IntPtr.Zero)
        {
            timer = NativeMethods.CreateWaitableTimerEx(IntPtr.Zero, null, 0, access);
        }

        if (timer == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create a precision playback timer.");
        }
    }

    internal void WaitMicroseconds(long microseconds)
    {
        if (microseconds <= 0)
        {
            return;
        }

        var dueTime = -Math.Max(1, checked(microseconds * 10));
        if (!NativeMethods.SetWaitableTimerEx(timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not arm the precision playback timer.");
        }

        var timeout = (uint)Math.Clamp(microseconds / 1_000 + 1_000, 1_000, uint.MaxValue - 1L);
        var result = NativeMethods.WaitForSingleObject(timer, timeout);
        if (result != NativeMethods.WaitObject0)
        {
            var error = result == NativeMethods.WaitFailed ? Marshal.GetLastWin32Error() : 0;
            throw new Win32Exception(error, "The precision playback timer did not complete normally.");
        }
    }

    public void Dispose()
    {
        if (timer != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(timer);
            timer = IntPtr.Zero;
        }
    }
}

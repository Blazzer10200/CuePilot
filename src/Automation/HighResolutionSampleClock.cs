using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CuePilot;

/// <summary>
/// Per-loop high-resolution wait shared by the Pickpocket, Fishing and Lockpicking
/// sample loops; no global timer-resolution change or spinning. A kernel wait from
/// this background process stretches by ~7 ms while the game runs (2026-09-26:
/// WaitOne(40) → 46.6 ms, Sleep(16) → 31 ms); this timer lands within 0.5 ms.
/// </summary>
internal sealed class HighResolutionSampleClock : IDisposable
{
    private sealed class TimerHandle(SafeWaitHandle handle) : WaitHandle
    {
        internal void Initialize() => SafeWaitHandle = handle;
    }
    private readonly TimerHandle timer;

    internal HighResolutionSampleClock()
    {
        // Unnamed, auto-reset high-resolution timer; Windows 10 1803+.
        var handle = CreateWaitableTimerExW(IntPtr.Zero, null, 2, 0x00100002);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error, "Could not create the sample timer."); }
        timer = new(handle);
        timer.Initialize();
    }

    internal static double NowMilliseconds => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;

    internal void WaitUntil(double deadlineMilliseconds, CancellationToken token)
        => WaitUntilCore(deadlineMilliseconds, token, yieldOnOverrun: true);

    internal void WaitForInputDeadline(double deadlineMilliseconds, CancellationToken token)
        => WaitUntilCore(deadlineMilliseconds, token, yieldOnOverrun: false);

    private void WaitUntilCore(double deadlineMilliseconds, CancellationToken token, bool yieldOnOverrun)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(deadlineMilliseconds)) throw new ArgumentOutOfRangeException(nameof(deadlineMilliseconds));
        var remaining = deadlineMilliseconds - NowMilliseconds;
        // Sampling yields on overruns to avoid catch-up bursts. An input deadline
        // must not inherit that extra millisecond or round a sub-ms wait upward.
        if (!yieldOnOverrun && remaining <= 0) return;
        var due = -(long)Math.Ceiling((yieldOnOverrun ? Math.Max(1, remaining) : remaining) * 10_000);
        if (!SetWaitableTimerEx(timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not arm the sample timer.");
        WaitHandle.WaitAny([timer, token.WaitHandle]);
        token.ThrowIfCancellationRequested();
    }

    public void Dispose() => timer.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, ref long dueTime, int period, IntPtr callback, IntPtr argument, IntPtr context, uint tolerance);
}

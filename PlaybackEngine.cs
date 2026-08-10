using System.ComponentModel;
using System.Diagnostics;

namespace WorkflowLooper;

internal sealed class PlaybackEngine
{
    private readonly HashSet<(int ScanCode, bool Extended)> downKeys = [];
    private readonly HashSet<int> downButtons = [];

    internal bool IsPlaying { get; private set; }
    internal double LastMaximumLatenessMilliseconds { get; private set; }
    internal double LastMeanLatenessMilliseconds { get; private set; }

    internal Task PlayAsync(
        WorkflowPattern pattern,
        int loopCount,
        decimal speed,
        bool trackCursor,
        CancellationToken cancellationToken)
    {
        if (IsPlaying)
        {
            throw new InvalidOperationException("Playback is already active.");
        }

        if (pattern.Events.Count == 0)
        {
            throw new InvalidOperationException("The workflow has no events to play.");
        }

        if (!WindowTargetService.IsForegroundMatch(pattern.TargetWindow, out var targetDetail))
        {
            throw new InvalidOperationException($"Playback blocked: {targetDetail}");
        }

        IsPlaying = true;
        return Task.Run(() =>
        {
            var originalPriority = Thread.CurrentThread.Priority;
            var noGcRegion = false;
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                noGcRegion = GC.TryStartNoGCRegion(16 * 1024 * 1024);
                using var waiter = new PrecisionWaiter();
                LastMaximumLatenessMilliseconds = 0;
                LastMeanLatenessMilliseconds = 0;
                double totalLatenessMicroseconds = 0;
                long timingSamples = 0;
                var iteration = 0;
                while (!cancellationToken.IsCancellationRequested && (loopCount == 0 || iteration < loopCount))
                {
                    PlayIteration(
                        pattern,
                        (double)speed,
                        trackCursor,
                        waiter,
                        cancellationToken,
                        ref totalLatenessMicroseconds,
                        ref timingSamples);
                    iteration++;
                }

                LastMeanLatenessMilliseconds = timingSamples == 0 ? 0 : totalLatenessMicroseconds / timingSamples / 1_000d;
            }
            finally
            {
                try
                {
                    ReleaseHeldInputs();
                }
                finally
                {
                    if (noGcRegion)
                    {
                        try
                        {
                            GC.EndNoGCRegion();
                        }
                        catch (InvalidOperationException)
                        {
                            // Windows or the runtime ended the region first; timing work is already complete.
                        }
                    }

                    Thread.CurrentThread.Priority = originalPriority;
                    IsPlaying = false;
                }
            }
        }, CancellationToken.None);
    }

    private void PlayIteration(
        WorkflowPattern pattern,
        double speed,
        bool trackCursor,
        PrecisionWaiter waiter,
        CancellationToken token,
        ref double totalLatenessMicroseconds,
        ref long timingSamples)
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (var item in pattern.Events)
        {
            if (!item.Enabled)
            {
                continue;
            }

            if (!WindowTargetService.IsForegroundMatch(pattern.TargetWindow, out var targetDetail))
            {
                throw new InvalidOperationException($"Playback stopped: {targetDetail}");
            }

            if (!trackCursor && item.Type == MacroEventType.MouseMove)
            {
                continue;
            }

            var target = item.OffsetMicroseconds / speed;
            WaitUntil(stopwatch, target, waiter, token);
            token.ThrowIfCancellationRequested();
            var actual = stopwatch.ElapsedTicks * 1_000_000d / Stopwatch.Frequency;
            var lateness = Math.Max(0, actual - target);
            totalLatenessMicroseconds += lateness;
            timingSamples++;
            LastMaximumLatenessMilliseconds = Math.Max(LastMaximumLatenessMilliseconds, lateness / 1_000d);
            SendEvent(pattern, item, trackCursor);
        }

        WaitUntil(stopwatch, pattern.DurationMicroseconds / speed, waiter, token);
    }

    internal static void WaitUntil(
        Stopwatch stopwatch,
        double targetMicroseconds,
        PrecisionWaiter waiter,
        CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var elapsed = stopwatch.ElapsedTicks * 1_000_000d / Stopwatch.Frequency;
            var remaining = targetMicroseconds - elapsed;
            if (remaining <= 0)
            {
                return;
            }

            if (remaining > 1_200)
            {
                waiter.WaitMicroseconds((long)Math.Min(remaining - 800, 10_000));
            }
            else
            {
                Thread.SpinWait(120);
            }
        }
    }

    private void SendEvent(WorkflowPattern pattern, MacroEvent item, bool trackCursor)
    {
        switch (item.Type)
        {
            case MacroEventType.KeyDown:
                SendKeyboard(item, false);
                downKeys.Add((item.ScanCode, item.Extended));
                break;
            case MacroEventType.KeyUp:
                SendKeyboard(item, true);
                downKeys.Remove((item.ScanCode, item.Extended));
                break;
            case MacroEventType.MouseMove:
                if (trackCursor)
                {
                    SendMouseMove(pattern, item.X, item.Y);
                }
                break;
            case MacroEventType.MouseDown:
                if (trackCursor)
                {
                    SendMouseMove(pattern, item.X, item.Y);
                }
                SendMouseButton(item.Data, false);
                downButtons.Add(item.Data);
                break;
            case MacroEventType.MouseUp:
                SendMouseButton(item.Data, true);
                downButtons.Remove(item.Data);
                break;
            case MacroEventType.MouseWheel:
                if (trackCursor)
                {
                    SendMouseMove(pattern, item.X, item.Y);
                }
                SendMouse(NativeMethods.MouseeventfWheel, unchecked((uint)item.Data));
                break;
            case MacroEventType.MouseHorizontalWheel:
                if (trackCursor)
                {
                    SendMouseMove(pattern, item.X, item.Y);
                }
                SendMouse(NativeMethods.MouseeventfHwheel, unchecked((uint)item.Data));
                break;
        }
    }

    private static void SendKeyboard(MacroEvent item, bool up)
    {
        var flags = NativeMethods.KeyeventfScancode;
        if (item.Extended)
        {
            flags |= NativeMethods.KeyeventfExtendedkey;
        }

        if (up)
        {
            flags |= NativeMethods.KeyeventfKeyup;
        }

        Send(
            new NativeMethods.Input
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeyboardInput
                    {
                        ScanCode = (ushort)item.ScanCode,
                        Flags = flags,
                    },
                },
            });
    }

    private static void SendMouseMove(WorkflowPattern pattern, int x, int y)
    {
        var current = SystemInformation.VirtualScreen;
        var scaled = ScalePoint(pattern, current, x, y);
        var normalizedX = (int)Math.Round((scaled.X - current.Left) * 65535d / Math.Max(1, current.Width - 1));
        var normalizedY = (int)Math.Round((scaled.Y - current.Top) * 65535d / Math.Max(1, current.Height - 1));
        SendMouse(NativeMethods.MouseeventfMove | NativeMethods.MouseeventfAbsolute | NativeMethods.MouseeventfVirtualdesk, 0, normalizedX, normalizedY);
    }

    internal static Point ScalePoint(WorkflowPattern pattern, Rectangle current, int x, int y)
    {
        var scaledX = current.Left + (int)Math.Round((x - pattern.RecordedLeft) * (current.Width - 1d) / Math.Max(1, pattern.RecordedWidth - 1));
        var scaledY = current.Top + (int)Math.Round((y - pattern.RecordedTop) * (current.Height - 1d) / Math.Max(1, pattern.RecordedHeight - 1));
        return new Point(scaledX, scaledY);
    }

    private static void SendMouseButton(int button, bool up)
    {
        var flags = (button, up) switch
        {
            (1, false) => NativeMethods.MouseeventfLeftdown,
            (1, true) => NativeMethods.MouseeventfLeftup,
            (2, false) => NativeMethods.MouseeventfRightdown,
            (2, true) => NativeMethods.MouseeventfRightup,
            (3, false) => NativeMethods.MouseeventfMiddledown,
            (3, true) => NativeMethods.MouseeventfMiddleup,
            (4 or 5, false) => NativeMethods.MouseeventfXdown,
            (4 or 5, true) => NativeMethods.MouseeventfXup,
            _ => throw new InvalidDataException($"Unsupported mouse button {button}."),
        };
        var data = button is 4 or 5 ? (uint)(button - 3) : 0;
        SendMouse(flags, data);
    }

    private static void SendMouse(uint flags, uint data, int x = 0, int y = 0)
    {
        Send(
            new NativeMethods.Input
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.InputUnion
                {
                    Mouse = new NativeMethods.MouseInput
                    {
                        X = x,
                        Y = y,
                        MouseData = data,
                        Flags = flags,
                    },
                },
            });
    }

    private static void Send(NativeMethods.Input input)
    {
        if (NativeMethods.SendInput(1, ref input, NativeMethods.InputSize) != 1)
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Windows rejected a simulated input event.");
        }
    }

    private void ReleaseHeldInputs()
    {
        foreach (var key in downKeys.ToArray())
        {
            SendKeyboard(new MacroEvent { ScanCode = key.ScanCode, Extended = key.Extended }, true);
        }

        foreach (var button in downButtons.ToArray())
        {
            SendMouseButton(button, true);
        }

        downKeys.Clear();
        downButtons.Clear();
    }
}
